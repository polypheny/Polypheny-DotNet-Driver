namespace PolyphenyDotNetDriver
{
    public class ProtoHelper
    {
        public static PolyphenyResult FormatResult(Polypheny.Prism.StatementResult result)
        {
            return result == null ? PolyphenyResult.Empty() : new PolyphenyResult(-1, result.Scalar);
        }
    }
}