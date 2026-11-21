/*
 * Copyright 2019-2024 The Polypheny Project
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Polypheny.Prism;

namespace PolyphenyDotNetDriver
{
    public static class ProtoHelper
    {
        public static PolyphenyResult FormatResult(Polypheny.Prism.StatementResult result)
        {
            return result == null ? PolyphenyResult.Empty() : new PolyphenyResult(-1, result.Scalar);
        }

        public static ProtoValue ToProtoValue(object value)
        {
            return value switch
            {
                int i => new ProtoValue { Integer = new ProtoInteger { Integer = i } },
                long l => new ProtoValue { Long = new ProtoLong() { Long = l } },
                float f => new ProtoValue() { Float = new ProtoFloat() { Float = f } },
                double d => new ProtoValue() { Double = new ProtoDouble() { Double = d } },
                string s => new ProtoValue() { String = new ProtoString() { String = s } },
                bool b => new ProtoValue() { Boolean = new ProtoBoolean() { Boolean = b } },
                _ => throw new ArgumentException("Unsupported value type: " + value.GetType().Name)
            };
        }

        public static object FromProtoValue(ProtoValue val)
        {
            if (val.Boolean != null)
            {
                return val.Boolean.Boolean;
            }
            if (val.Integer != null)
            {
                return val.Integer.Integer;
            }
            if (val.Long != null)
            {
                return val.Long.Long;
            }
            if (val.Float != null)
            {
                return val.Float.Float;
            }
            if (val.Double != null)
            {
                return val.Double.Double;
            }
            if (val.BigDecimal != null)
            {
                return new PolyphenyDecimal(
                    new BigInteger(val.BigDecimal.UnscaledValue.Span, isUnsigned: false, isBigEndian: true),
                    val.BigDecimal.Scale);
            }
            if (val.String != null)
            {
                return val.String.String;
            }
            if (val.Binary != null)
            {
                return val.Binary.Binary.ToByteArray();
            }
            if (val.File != null)
            {
                return val.File.Binary.ToByteArray();
            }
            if (val.Date != null)
            {
                return DateTime.UnixEpoch.AddDays(val.Date.Date);
            }
            if (val.Time != null)
            {
                return TimeSpan.FromMilliseconds(val.Time.Time);
            }
            if (val.Timestamp != null)
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(val.Timestamp.Timestamp).UtcDateTime;
            }
            if (val.Interval != null)
            {
                return new PolyphenyInterval(val.Interval.Months, val.Interval.Milliseconds);
            }
            if (val.Null != null)
            {
                return null;
            }
            if (val.List != null)
            {
                return val.List.Values.Select(FromProtoValue).ToArray();
            }
            if (val.Document != null)
            {
                return FromProtoDocument(val.Document);
            }
            if (val.Node != null)
            {
                return new GraphNode(val.Node);
            }
            if (val.Edge != null)
            {
                return new GraphEdge(val.Edge);
            }
            if (val.Path != null)
            {
                return new GraphPath(val.Path);
            }
            if (val.Graph != null)
            {
                return new GraphValue(val.Graph);
            }

            throw new NotSupportedException("The Prism value has no supported value case.");
        }

        internal static Dictionary<object, object> FromProtoDocument(ProtoDocument document)
        {
            var values = new Dictionary<object, object>(document.Entries.Count);
            foreach (var entry in document.Entries)
            {
                values.Add(entry.Key, FromProtoValue(entry.Value));
            }
            return values;
        }
    }
}
