using System;
using System.Collections.Generic;
using System.Text;

namespace TextToSqlAgent.Infrastructure.Configuration
{
    public class GeminiConfig
    {
        public string ApiKey { get; set; } = string.Empty;
        public string Model { get; set; } = "gemini-2.5-flash";
        public string EmbeddingModel { get; set; } = "gemini-embedding-1.0";
        public int MaxTokens { get; set; } = 8192;
        public double Temperature { get; set; } = 0.1;
    }
}
