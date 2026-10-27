// Licensed under the MIT license. See LICENSE in the project root.

using System;
using Sedhep.Domain;

namespace Sedhep.Infrastructure;

public sealed class UtcClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
