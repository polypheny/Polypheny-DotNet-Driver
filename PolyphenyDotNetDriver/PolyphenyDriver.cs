using Polypheny.Prism;

namespace PolyphenyDotNetDriver
{
    public class PolyphenyDriver
    {
        public string Name { get; set; }

        public PolyphenyDriver()
        {
            Name = "PolyphenyDotNetDriver";
        }

        public string GetDriverName()
        {
            var protoString = new ProtoString{
                String = Name
            };
            return protoString.String;
        }
    }
}
