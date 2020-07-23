using InspectionCenterService.Domain.Entities;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Dynamic;
using System.Linq;
using System.Threading.Tasks;

namespace InspectionCenterService.Application.Dto
{
    public class InspectionCenterDto
    {

        public Guid Id { get; set; }
        [Required]
        [RegularExpression(@"^[ء-يa-zA-Z0-9-&-_@]{1,150}$", ErrorMessage = "Enter valid value for Center name")]
        public string Name { get; set; }
        [Required]
        [RegularExpression(@"^[a-zA-Z0-9]{1,150}$", ErrorMessage = "Enter valid value for RefCode")]
        public string RefCode { get; set; }
        public JArray InspectionCenterCoverages { get; set; }
        public JArray RoleEmails { get; set; }

        public virtual Guid? CreatorUserId { get; set; }
        public virtual DateTime CreationTime { get; set; }
        public virtual Guid? DeleterUserId { get; set; }
        public virtual DateTime? DeletionTime { get; set; }
        public virtual bool IsDeleted { get; set; }
        public virtual DateTime? LastModificationTime { get; set; }
        public virtual Guid? LastModifierUserId { get; set; }
    }

    public sealed class QaiserCustomType2 : JsonConverter
    {
        public override bool CanConvert(Type customType)
        {
            bool canBeCoverted = false;
            if (typeof(JArray).IsAssignableFrom(customType) || typeof(JObject).IsAssignableFrom(customType) || typeof(ExpandoObject).IsAssignableFrom(customType))
                canBeCoverted = true;

            return canBeCoverted;
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var isExpando = typeof(ExpandoObject).IsAssignableFrom(objectType);
           
            if (reader.TokenType == JsonToken.StartObject)
            {
                var rootObject = Activator.CreateInstance(objectType) as ExpandoObject as IDictionary<string, object>;
                JObject item = JObject.Load(reader);
                IEnumerable<JProperty> objectProperties = item.Properties();
                foreach (JProperty prop in objectProperties)
                {

                    if (prop.Value.Type.ToString().ToLower() == "string")
                    {
                        JToken jToken = prop.Value;
                        try
                        {
                            jToken = JToken.Parse(prop.Value.ToString());
                        }
                        catch { }
                        rootObject.Add(prop.Name.ToString(), jToken);
                    }
                }
                return rootObject;
            }
            else if (reader.TokenType == JsonToken.StartArray)
            {
                var path = reader.Path;
                var propertyName = path.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();

                JArray item = JArray.Load(reader);
                return item;
            }
            return "s";
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }
}
