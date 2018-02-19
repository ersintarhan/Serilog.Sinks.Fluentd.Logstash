using System;

namespace Serilog.Sinks.Fluentd.Logstash
{
    public class FluentdHandlerSettings
    {
        public string Tag { get; set; } = "";

        public string Host { get; set; } = "localhost";

        public int Port { get; set; } = 24224;

        public int TcpSendTimeout { get; set; } = 3000;

        public TimeSpan BatchingPeriod { get; set; } = TimeSpan.FromSeconds(2);

        public int BatchPostingLimit { get; set; } = 50;

        public int TcpRetryAmount { get; set; } = 5;
    }
}
