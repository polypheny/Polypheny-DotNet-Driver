using System;
using Google.Protobuf.WellKnownTypes;
using Polypheny.Prism;

namespace PolyphenyDotNetDriver
{
    public class ProtoHelper
    {
        public static PolyphenyResult FormatResult(Polypheny.Prism.StatementResult result)
        {
            return result == null ? PolyphenyResult.Empty() : new PolyphenyResult(-1, result.Scalar);
        }

        public static ProtoValue ToProtoValue(object value)
        {
            switch (value)
            {
                case int i:
                    return new ProtoValue { Integer = new ProtoInteger { Integer = i } };
                case long l:
                    return new ProtoValue { Long = new ProtoLong() { Long = l } };
                case float f:
                    return new ProtoValue() { Float = new ProtoFloat() { Float = f } };
                case double d:
                    return new ProtoValue() { Double = new ProtoDouble() { Double = d } };
                case string s:
                    return new ProtoValue() { String = new ProtoString() { String = s } };
                case bool b:
                    return new ProtoValue() { Boolean = new ProtoBoolean() { Boolean = b } };
                default:
                    throw new ArgumentException("Unsupported value type: " + value.GetType().Name);
            }
        }
    }
}