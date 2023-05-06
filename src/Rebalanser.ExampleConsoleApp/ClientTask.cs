namespace Rebalanser.RabbitMq.ExampleWithSqlServerBackend;

using System.Threading;
using System.Threading.Tasks;

public class ClientTask
{
    public CancellationTokenSource Cts { get; set; }
    public Task Client { get; set; }
}
