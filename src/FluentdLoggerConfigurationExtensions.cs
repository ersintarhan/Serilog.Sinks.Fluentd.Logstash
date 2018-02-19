using System;
using Serilog.Configuration;
using Serilog.Sinks.Fluentd.Logstash;
using Serilog.Sinks.Fluentd.Logstash.Sinks;

// ReSharper disable once CheckNamespace
namespace Serilog.Sinks
{
    public static class FluentdLoggerConfigurationExtensions
    {
        public static LoggerConfiguration Fluentd(this LoggerSinkConfiguration sinkConfiguration, FluentdHandlerSettings settings)
        {
            if (sinkConfiguration == null)
            {
                throw new ArgumentNullException(nameof(sinkConfiguration));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            return sinkConfiguration.Sink(new FluentdSink(settings));
        }
    }
}
