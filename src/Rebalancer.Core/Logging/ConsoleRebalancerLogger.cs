using System;

namespace Rebalancer.Core.Logging;

/// <summary>Temporary hack</summary>
public class ConsoleRebalancerLogger : IRebalancerLogger
{
    private LogLevel minimumLevel;

    public ConsoleRebalancerLogger()
    {
        minimumLevel = LogLevel.DEBUG;
    }

    public ConsoleRebalancerLogger(LogLevel logLevel)
    {
        minimumLevel = logLevel;
    }

    public void Error(string clientId, string text)
    {
        if ((int)minimumLevel <= 3)
        {
            Console.WriteLine($"{DateTime.Now.ToString("hh:mm:ss,fff")}: ERROR : {clientId} : {text}");
        }
    }

    public void Error(string clientId, Exception ex)
    {
        if ((int)minimumLevel <= 3)
        {
            Console.WriteLine($"{DateTime.Now.ToString("hh:mm:ss,fff")}: ERROR : {clientId} : {ex}");
        }
    }

    public void Error(string clientId, string text, Exception ex)
    {
        if ((int)minimumLevel <= 3)
        {
            Console.WriteLine($"{DateTime.Now.ToString("hh:mm:ss,fff")}: ERROR : {clientId} : {text} : {ex}");
        }
    }

    public void Warn(string clientId, string text)
    {
        if ((int)minimumLevel <= 2)
        {
            Console.WriteLine($"{DateTime.Now.ToString("hh:mm:ss,fff")}: WARN : {clientId} : {text}");
        }
    }

    public void Info(string clientId, string text)
    {
        if ((int)minimumLevel <= 1)
        {
            Console.WriteLine($"{DateTime.Now.ToString("hh:mm:ss,fff")}: INFO  : {clientId} : {text}");
        }
    }

    public void Debug(string clientId, string text)
    {
        if (minimumLevel == 0)
        {
            Console.WriteLine($"{DateTime.Now.ToString("hh:mm:ss,fff")}: DEBUG : {clientId} : {text}");
        }
    }

    public void SetMinimumLevel(LogLevel logLevel) => minimumLevel = logLevel;
}
