namespace PolyphenyDotNetDriver;

public class PolyphenyConfig
{
    public string Database { get; set; } = "PolyphenyDatabase";
    public string DataSource { get; set; } = "PolyphenyDataSource";
    public string TransportVersion { get; set; } = "plain-v1@polypheny.com\n";
}
