namespace TextToSqlAgent.Infrastructure.Templates;

public static class SqlTemplates
{
    public static string CountWithHaving = @"
SELECT 
    {entity}.{id_column},
    {entity}.{name_column},
    COUNT({related}.{id_column}) as {related}Count
FROM {entity}
JOIN {related} ON {entity}.{id_column} = {related}.{fk_column}
GROUP BY {entity}.{id_column}, {entity}.{name_column}
HAVING COUNT({related}.{id_column}) {operator} {value}
ORDER BY {related}Count DESC;
";

    public static string TopNByMetric = @"
SELECT TOP {n}
    {entity}.{id_column},
    {entity}.{name_column},
    {aggregation}({metric_column}) as {metric_name}
FROM {entity}
JOIN {related} ON {entity}.{id_column} = {related}.{fk_column}
GROUP BY {entity}.{id_column}, {entity}.{name_column}
ORDER BY {metric_name} DESC;
";

    public static string SimpleAggregation = @"
SELECT 
    {aggregation}({column}) as {result_name}
FROM {table}
{where_clause};
";

    public static string SimpleCount = @"
SELECT 
    COUNT(*) as Total
FROM {table}
{where_clause};
";

    public static string SimpleList = @"
SELECT 
    {columns}
FROM {table}
{where_clause}
{order_clause}
{limit_clause};
";

    public static string GroupByAggregation = @"
SELECT 
    {group_columns},
    {aggregations}
FROM {table}
{join_clause}
{where_clause}
GROUP BY {group_columns}
{having_clause}
{order_clause}
{limit_clause};
";
}
