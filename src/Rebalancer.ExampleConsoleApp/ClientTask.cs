using System.Threading;
using System.Threading.Tasks;

namespace Rebalancer.RabbitMq.ExampleWithSqlServerBackend;

public class ClientTask
{
    public CancellationTokenSource Cts { get; set; }
    public Task Client { get; set; }
}
