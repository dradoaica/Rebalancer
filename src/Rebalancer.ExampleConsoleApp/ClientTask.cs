using System.Threading;
using System.Threading.Tasks;

namespace Rebalancer.RabbitMq.ExampleWithSqlServerBackend;

public class ClientTask
{
    public CancellationTokenSource Cts { get; init; }
    public Task Client { get; init; }
}
