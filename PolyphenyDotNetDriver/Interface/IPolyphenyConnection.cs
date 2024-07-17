using System.Threading.Tasks;
using Polypheny.Prism;

namespace PolyphenyDotNetDriver.Interface;

public interface IPolyphenyConnection
{
    public Task<Response> SendRecv(Request m);
    public Task<Response> Receive(int lengthSize=8);
}
