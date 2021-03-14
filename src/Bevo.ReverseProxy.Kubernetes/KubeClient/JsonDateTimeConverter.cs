// Copyright (c) 2021 David Bevin
//
// This software is released under the MIT License.
// https://opensource.org/licenses/MIT

using System;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bevo.ReverseProxy.Kube
{
    public class JsonDateTimeConverter : JsonConverter
    {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var dt = (DateTime)value;

            serializer.Serialize(writer, dt.ToString("yyyy-MM-ddTHH:mm:ss.ffffffK", CultureInfo.InvariantCulture));
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var obj = JToken.Load(reader);

            try
            {
                return obj.ToObject(objectType);
            }
            catch (JsonException)
            {
                // should be an object
            }

            // Hope we never get here!
            return new DateTime();
        }

        public override bool CanConvert(Type objectType)
        {
            return typeof(DateTime) == objectType;
        }
    }
}
