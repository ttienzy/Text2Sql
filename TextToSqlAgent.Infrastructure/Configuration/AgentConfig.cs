using System;
using System.Collections.Generic;
using System.Text;

namespace TextToSqlAgent.Infrastructure.Configuration
{
    public class AgentConfig
    {
        public int MaxSelfCorrectionAttempts { get; set; } = 3;
        public bool EnableSQLExplanation { get; set; } = true;
        public bool EnableDetailedLogging { get; set; } = true;
    }
}
