using Polypheny.Prism;

namespace PolyphenyDotNetDriver
{
    public class PolyphenyDriver
    {        
        public PolyphenyConnection Open(string connectionString)
        {
            return new PolyphenyConnection(connectionString);
        }
    }
}
