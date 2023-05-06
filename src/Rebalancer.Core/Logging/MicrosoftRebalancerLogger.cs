namespace Rebalancer.Core.Logging;

using System;
using Microsoft.Extensions.Logging;

/// <summary>
///     Temporary hack
/// </summary>
public class MicrosoftRebalancerLogger : IRebalancerLogger
{
    private readonly ILogger logger;
    private LogLevel logLevel;

    public MicrosoftRebalancerLogger(ILogger logger)
    {
        this.logger = logger;
        this.logLevel = LogLevel.DEBUG;
    }

    public MicrosoftRebalancerLogger(ILogger logger, LogLevel logLevel)
    {
        this.logger = logger;
        this.logLevel = logLevel;
    }

    public void Error(string clientId, string text)
    {
        if ((int)this.logLevel <= 3)
        {
            this.logger?.LogError($"{DateTime.Now.ToString("hh:mm:ss,fff")}: ERROR : {clientId} : {text}");
        }
    }

    public void Error(string clientId, Exception ex)
    {
        if ((int)this.logLevel <= 3)
        {
            this.logger?.LogError($"{DateTime.Now.ToString("hh:mm:ss,fff")}: ERROR : {clientId} : {ex}");
        }
    }

    public void Error(string clientId, string text, Exception ex)
    {
        if ((int)this.logLevel <= 3)
        {
            this.logger?.LogError($"{DateTime.Now.ToString("hh:mm:ss,fff")}: ERROR : {clientId} : {text} : {ex}");
        }
    }

    public void Warn(string clientId, string text)
    {
        if ((int)this.logLevel <= 2)
        {
            this.logger?.LogWarning($"{DateTime.Now.ToString("hh:mm:ss,fff")}: WARN : {clientId} : {text}");
        }
    }

    public void Info(string clientId, string text)
    {
        if ((int)this.logLevel <= 1)
        {
            this.logger?.LogInformation($"{DateTime.Now.ToString("hh:mm:ss,fff")}: INFO  : {clientId} : {text}");
        }
    }

    public void Debug(string clientId, string text)
    {
        if (this.logLevel == 0)
        {
            this.logger?.LogDebug($"{DateTime.Now.ToString("hh:mm:ss,fff")}: DEBUG : {clientId} : {text}");
        }
    }

    public void SetMinimumLevel(LogLevel logLevel) => this.logLevel = logLevel;
}
