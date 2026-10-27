// Licensed under the MIT license. See LICENSE in the project root.

using System;

namespace Sedhep.Domain;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
