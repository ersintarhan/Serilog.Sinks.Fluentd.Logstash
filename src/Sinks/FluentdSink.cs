using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Formatting.Json;
using Serilog.Parsing;
using Serilog.Sinks.PeriodicBatching;

namespace Serilog.Sinks.Fluentd.Logstash.Sinks
{
    public class FluentdSink : PeriodicBatchingSink
    {
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1);
        private static readonly long EpochTicks = new DateTime(1970, 1, 1).Ticks;

        private readonly FluentdHandlerSettings _settings;

        private TcpClient _client;

        public FluentdSink(FluentdHandlerSettings settings) : base(settings.BatchPostingLimit, settings.BatchingPeriod)
        {
            this._settings = settings;
        }

        protected override async Task EmitBatchAsync(IEnumerable<LogEvent> events)
        {
            await this.Send(events);
        }

        private async Task Connect()
        {
            if (this._client != null)
            {
                return;
            }

            this._client = new TcpClient { SendTimeout = this._settings.TcpSendTimeout };
            await this._client.ConnectAsync(this._settings.Host, this._settings.Port);
        }

        private async Task Send(IEnumerable<LogEvent> messages)
        {
            foreach (var logEvent in messages)
            {
                using (var sw = new StringWriter())
                {
                    try
                    {
                        this.Format(logEvent, sw);
                    }
                    catch (Exception ex)
                    {
                        SelfLog.WriteLine(ex.ToString());
                        continue;
                    }

                    var serialized = $"[\"{this._settings.Tag}\",{logEvent.Timestamp.ToUnixTimeSeconds()},{sw}]";
                    var encoded = Encoding.UTF8.GetBytes(serialized);
                    var retryLimit = this._settings.TcpRetryAmount;

                    while (retryLimit > 0)
                    {
                        try
                        {
                            await this.Connect();
                            await this._semaphore.WaitAsync();
                            await this._client.GetStream().WriteAsync(encoded, 0, encoded.Length);
                            await this._client.GetStream().FlushAsync();
                            break;
                        }
                        catch (Exception ex)
                        {
                            this.Disconnect();
                            SelfLog.WriteLine(ex.ToString());
                        }
                        finally
                        {
                            this._semaphore.Release();
                            retryLimit--;
                        }
                    }
                }
            }
        }

        private void Format(LogEvent logEvent, StringWriter output)
        {
            if (logEvent == null)
            {
                throw new ArgumentNullException(nameof(logEvent));
            }
            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            output.Write("{\"utctime\":\"");
            output.Write(logEvent.Timestamp.UtcDateTime.ToString("O"));
            output.Write("\",\"unixtime\":");
            output.Write((logEvent.Timestamp.UtcTicks - EpochTicks) / TimeSpan.TicksPerSecond);

            
            
            var tokensWithFormat = logEvent.MessageTemplate.Tokens
                .OfType<PropertyToken>()
                .Where(pt => pt.Format != null);

            
            if (tokensWithFormat.Any())
            {
                output.Write(",\"@r\":[");
                var delim = "";
                foreach (var token in tokensWithFormat)
                {
                    output.Write(delim);
                    delim = ",";
                    var space = new StringWriter();
                    token.Render(logEvent.Properties, space);
                    JsonValueFormatter.WriteQuotedJsonString(space.ToString(), output);
                }
                output.Write(']');
            }

            output.Write(",\"level\":\"");
            output.Write(logEvent.Level);
            output.Write('\"');

            if (logEvent.Exception != null)
            {
                output.Write(',');
                this.WriteException(logEvent.Exception, output);
            }

            foreach (var property in logEvent.Properties)
            {
                var name = property.Key;
                if (name.Length > 0 && name[0] == '@')
                {
                    // Escape first '@' by doubling
                    name = '@' + name;
                }

                output.Write(',');
                JsonValueFormatter.WriteQuotedJsonString(name, output);
                output.Write(':');
                new JsonValueFormatter("$type").Format(property.Value, output);
            }

            output.Write('}');
        }

        /// <summary>
        /// Writes out the attached exception
        /// </summary>
        private void WriteException(Exception exception, TextWriter output)
        {
            output.Write("\"");
            output.Write("exceptions");
            output.Write("\":[");

            this.WriteExceptionSerializationInfo(exception, output, 0);
            output.Write("]");
        }

        private void WriteExceptionSerializationInfo(Exception exception, TextWriter output, int depth)
        {
            if (depth > 0)
            {
                output.Write(",");
            }
            output.Write("{");
            this.WriteSingleException(exception, output, depth);
            output.Write("}");

            if (exception.InnerException != null && depth < 20)
            {
                this.WriteExceptionSerializationInfo(exception.InnerException, output, ++depth);
            }
        }

        /// <summary>
        /// Writes the properties of a single exception, without inner exceptions
        /// Callers are expected to open and close the json object themselves.
        /// </summary>
        /// <param name="exception"></param>
        /// <param name="output"></param>
        /// <param name="depth"></param>
        private void WriteSingleException(Exception exception, TextWriter output, int depth)
        {
            var helpUrl = exception.HelpLink;
            var stackTrace = exception.StackTrace ?? "";
            var hresult = exception.HResult;
            var source = exception.Source;

            this.WriteJsonProperty("Depth", depth, ",", output);
            this.WriteJsonProperty("Message", exception.Message, ",", output);
            this.WriteJsonProperty("Source", source, ",", output);

            output.Write("\"StackTraceString\":");
            JsonValueFormatter.WriteQuotedJsonString(stackTrace, output);
            output.Write(",");
            this.WriteJsonProperty("HResult", hresult, ",", output);
            this.WriteJsonProperty("HelpURL", helpUrl, "", output);
        }

        private void WriteJsonProperty(string propName, object value, string delim, TextWriter output)
        {
            output.Write("\"" + propName + "\":\"" + value + "\"" + delim);
        }

        private void Disconnect()
        {
            this._client?.Dispose();
            this._client = null;
        }

        protected override void Dispose(bool disposing)
        {
            this.Disconnect();

            base.Dispose(disposing);
        }
    }
}
