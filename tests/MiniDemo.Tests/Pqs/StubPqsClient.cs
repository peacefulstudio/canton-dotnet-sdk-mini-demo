// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Splice.Api.Token.HoldingV2;
using DemoAsset = MiniDemo.Asset.Asset;

namespace MiniDemo.Tests;

internal sealed class StubPqsClient : IPqsClient
{
    public Func<CancellationToken, Task<Contract<DemoAsset>?>>? FetchByIdBehavior { get; set; }

    public Func<CancellationToken, Task<IReadOnlyList<Contract<DemoAsset>>>>? QueryBehavior { get; set; }

    public Task<IReadOnlyList<Contract<T>>> QueryAsync<T>(CancellationToken cancellationToken)
        where T : ITemplate, IDamlRecord<T> => throw new NotSupportedException();

    public Task<IReadOnlyList<InterfaceContract<TInterface, TView>>> QueryAsync<TInterface, TView>(
        CancellationToken cancellationToken)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> => throw new NotSupportedException();

    public Task<IReadOnlyList<Contract<T>>> QueryAsync<T>(PqsFilter filter, CancellationToken cancellationToken)
        where T : ITemplate, IDamlRecord<T> => throw new NotSupportedException();

    public Task<IReadOnlyList<Contract<T>>> QueryAsync<T>(PqsPage page, CancellationToken cancellationToken)
        where T : ITemplate, IDamlRecord<T> => throw new NotSupportedException();

    public Task<IReadOnlyList<InterfaceContract<TInterface, TView>>> QueryAsync<TInterface, TView>(
        PqsPage page, CancellationToken cancellationToken)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> => throw new NotSupportedException();

    public Task<IReadOnlyList<Contract<T>>> QueryAsync<T>(
        PqsFilter filter, PqsPage page, CancellationToken cancellationToken)
        where T : ITemplate, IDamlRecord<T>
    {
        if (typeof(T) == typeof(DemoAsset) && QueryBehavior is { } behavior)
            return (Task<IReadOnlyList<Contract<T>>>)(object)behavior(cancellationToken);
        throw new NotSupportedException();
    }

    public Task<Contract<T>?> QueryOneAsync<T>(PqsFilter filter, CancellationToken cancellationToken)
        where T : ITemplate, IDamlRecord<T> => throw new NotSupportedException();

    public Task<Contract<T>?> FetchByIdAsync<T>(ContractId<T> contractId, CancellationToken cancellationToken)
        where T : ITemplate, IDamlRecord<T>
    {
        if (typeof(T) == typeof(DemoAsset) && FetchByIdBehavior is { } behavior)
            return (Task<Contract<T>?>)(object)behavior(cancellationToken);
        throw new NotSupportedException();
    }

    public Task<InterfaceContract<TInterface, TView>?> FetchByIdAsync<TInterface, TView>(
        ContractId<TInterface> contractId, CancellationToken cancellationToken)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> => throw new NotSupportedException();

    public Task<bool> ExistsAsync<T>(ContractId<T> contractId, CancellationToken cancellationToken)
        where T : ITemplate => throw new NotSupportedException();
}
