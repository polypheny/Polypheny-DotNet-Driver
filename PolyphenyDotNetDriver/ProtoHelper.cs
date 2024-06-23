using System;
using Google.Protobuf.WellKnownTypes;
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
                // TODO: add support for big decimal
                // seems like need external library
                return null;
            }
            if (val.String != null)
            {
                return val.String.String;
            }

            throw new Exception("Failed to convert ProtoValue. This is likely a bug.");
        }
    }
}