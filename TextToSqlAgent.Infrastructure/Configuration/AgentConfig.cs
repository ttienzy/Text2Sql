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

        /// <summary>
        /// Enable query validation to filter out-of-scope queries
        /// </summary>
        public bool EnableQueryValidation { get; set; } = true;

        /// <summary>
        /// Enable multi-turn conversation support
        /// </summary>
        public bool EnableConversationContext { get; set; } = true;

        /// <summary>
        /// Explain queries before execution
        /// </summary>
        public bool ExplainQueriesBeforeExecution { get; set; } = false;

        /// <summary>
        /// Maximum conversation history size
        /// </summary>
        public int MaxConversationHistorySize { get; set; } = 10;

        /// <summary>
        /// Conversation timeout in minutes
        /// </summary>
        public int ConversationTimeoutMinutes { get; set; } = 30;
    }
}
