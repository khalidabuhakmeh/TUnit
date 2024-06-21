﻿namespace TUnit.Engine;

public class TimedCancellationToken
{
    private readonly TimeSpan _timeout;

    public TimedCancellationToken(TimeSpan timeout)
    {
        _timeout = timeout;
    }

    public static implicit operator CancellationToken(TimedCancellationToken cancellationToken)
    {
        var newCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(EngineCancellationToken.CancellationTokenSource.Token);
        newCancellationTokenSource.CancelAfter(cancellationToken._timeout);
        return newCancellationTokenSource.Token;
    }
}