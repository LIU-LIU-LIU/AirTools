// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See THIRD_PARTY_NOTICES.md in the project root for more information.

using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.CommandPalette.Extensions;

namespace AirTools.CommandPalette;

[Guid("A73629DC-03E4-4EA8-8AB3-38DF28AF420B")]
public sealed partial class AirToolsExtension : IExtension, IDisposable
{
    private readonly ManualResetEvent _extensionDisposedEvent;

    private readonly AirToolsCommandsProvider _provider = new();

    public AirToolsExtension(ManualResetEvent extensionDisposedEvent)
    {
        this._extensionDisposedEvent = extensionDisposedEvent;
    }

    public object? GetProvider(ProviderType providerType)
    {
        return providerType switch
        {
            ProviderType.Commands => _provider,
            _ => null,
        };
    }

    public void Dispose() { _provider.Dispose(); _extensionDisposedEvent.Set(); }
}
