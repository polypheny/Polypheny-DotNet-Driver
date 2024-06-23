using Polypheny.Prism;

namespace PolyphenyDotNetDriver
{
    public class PolyphenyDriver
    {        
        public static PolyphenyConnection OpenConnection(string connectionString)
        {
            return new PolyphenyConnection(connectionString);
        }
    }
}
