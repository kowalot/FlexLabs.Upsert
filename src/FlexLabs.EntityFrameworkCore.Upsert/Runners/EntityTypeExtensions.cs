using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.EntityFrameworkCore.Metadata;

namespace FlexLabs.EntityFrameworkCore.Upsert.Runners
{
    public static class EntityTypeExtensions
    {
        public static string GetTableName(this IEntityType entityType)
        {
            var annotations = entityType.GetAnnotations();
            return annotations.FirstOrDefault(t => t.Name == "Relational:TableName")?.Value.ToString();
        }

        public static string GetSchemaName(this IEntityType entityType)
        {
            var annotations = entityType.GetAnnotations();
            return annotations.FirstOrDefault(t => t.Name == "Relational:Schema")?.Value.ToString();
        }

        public static string GetColumnName1(this IProperty property)
        {
            var annotations = property.GetAnnotations();
            throw new NotImplementedException();
        }
    }
}
