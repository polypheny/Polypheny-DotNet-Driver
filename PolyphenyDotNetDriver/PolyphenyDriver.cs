using Polypheny.Prism;

namespace PolyphenyDotNetDriver
{
    public class PolyphenyDriver
    {        
        public static PolyphenyConnection OpenConnection(string connectionString)
        {
            var config = new PolyphenyConfig();
            return new PolyphenyConnection(connectionString, config);
        }
    }
}
