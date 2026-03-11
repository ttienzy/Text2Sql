using TextToSqlAgent.Infrastructure.Templates;

namespace TextToSqlAgent.Infrastructure.Generation;

public class TemplateBasedSqlGenerator
{
    public string Generate(string templateName, Dictionary<string, string> parameters)
    {
        var template = GetTemplate(templateName);

        foreach (var (key, value) in parameters)
        {
            template = template.Replace($"{{{key}}}", value);
        }

        return template.Trim();
    }

    private string GetTemplate(string templateName)
    {
        return templateName switch
        {
            "COUNT_HAVING" => SqlTemplates.CountWithHaving,
            "TOP_N" => SqlTemplates.TopNByMetric,
            "SIMPLE_AGG" => SqlTemplates.SimpleAggregation,
            "SIMPLE_COUNT" => SqlTemplates.SimpleCount,
            "SIMPLE_LIST" => SqlTemplates.SimpleList,
            "GROUP_BY_AGG" => SqlTemplates.GroupByAggregation,
            _ => throw new ArgumentException($"Unknown template: {templateName}")
        };
    }

    public bool SupportsTemplate(string templateName)
    {
        return templateName switch
        {
            "COUNT_HAVING" or "TOP_N" or "SIMPLE_AGG" or "SIMPLE_COUNT" or "SIMPLE_LIST" or "GROUP_BY_AGG" => true,
            _ => false
        };
    }
}
