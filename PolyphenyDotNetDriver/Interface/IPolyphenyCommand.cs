using System.Threading.Tasks;
using Polypheny.Prism;

namespace PolyphenyDotNetDriver.Interface;

public interface IPolyphenyCommand
{
    public Task<Response> SendRecv(Request m);
    public Task<Response> Receive(int lengthSize);
    public string CommandText { get; }
}
