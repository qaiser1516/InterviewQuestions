using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;

namespace InspectionCenterService.Domain.Entities
{

    public static class CustomSchemaHelper<T>
    {
        public static string Serialize(object objectToSerialize)
        {
            return JsonConvert.SerializeObject(objectToSerialize);
        }

        /// <summary>
        /// Deserialze an object into json string
        /// </summary>
        /// <param name="objectToDeSerialize"></param>
        /// <returns></returns>
        public static T Deserialize(string objectToDeSerialize)
        {
            return JsonConvert.DeserializeObject<T>(objectToDeSerialize, new CustomJsonSchemaDeserializer());
        }

        public static T DeserializeComplex(object objectToDeSerialize)
        {
            var serializedObject = Serialize(objectToDeSerialize);
            return JsonConvert.DeserializeObject<T>(serializedObject, new CustomJsonSchemaDeserializer());
        }
    }
    public sealed class CustomJsonSchemaDeserializer : JsonConverter
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

            if(isExpando)
            {
                var rootObject = Activator.CreateInstance(objectType) as ExpandoObject as IDictionary<string, object>;
                JObject item = JObject.Load(reader);
                IEnumerable<JProperty> objectProperties = item.Properties();
                foreach (JProperty prop in objectProperties)
                {
                    JToken jToken = prop.Value;
                    try
                    {
                        jToken = JToken.Parse(prop.Value.ToString());
                    }
                    catch { }
                    rootObject.Add(prop.Name.ToString(), jToken);
                }
                return rootObject;
            }           
            else if (reader.TokenType == JsonToken.StartArray)
            {
                JArray item = JArray.Load(reader);
                return item;
            }
            else
            {
                JObject item = JObject.Load(reader);
                return item;
            }
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }
}
