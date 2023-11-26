using CommunicationLibrary;
using CommunicationLibrary.Bank;
using CommunicationLibrary.Enums;
using CommunicationLibrary.Models;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Remoting.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using Newtonsoft.Json;
using System.Fabric;

namespace Bank
{
    internal sealed class Bank : StatefulService, IBank, CommunicationLibrary.TransactionCoordinator.ITransaction
    {
        private IReliableDictionary<long, Client>? _clientDictionary;

        public Bank(StatefulServiceContext context)
            : base(context)
        { }

        private async Task InitializeClientDictionaryAsync()
        {
            _clientDictionary = await StateManager.GetOrAddAsync<IReliableDictionary<long, Client>>("clientDictionary");
        }

        #region IBankImplementation

        public async Task<List<string>> ListClients()
        {
            await InitializeClientDictionaryAsync();

            var clients = new List<Client>()
            {
                new Client() { Id = 1, FirstName = "John", LastName = "Doe", DateOfBirth = new DateTime(1990, 1, 1), BankName = "Horizon Bank", BankAccount = 1000.00, BankMembership = BankMembership.Gold.GetDescription() },
                new Client() { Id = 2, FirstName = "Jane", LastName = "Smith", DateOfBirth = new DateTime(1985, 5, 15), BankName = "Unity Trust", BankAccount = 750.50, BankMembership = BankMembership.Silver.GetDescription() },
                new Client() { Id = 3, FirstName = "Bob", LastName = "Johnson", DateOfBirth = new DateTime(1995, 10, 8), BankName = "Capital Haven Bank", BankAccount = 500.75, BankMembership = BankMembership.Bronze.GetDescription() },
                new Client() { Id = 4, FirstName = "Alice", LastName = "Williams", DateOfBirth = new DateTime(1982, 3, 20), BankName = "Stellar Finance", BankAccount = 2000.25, BankMembership = BankMembership.Platinum.GetDescription() },
                new Client() { Id = 5, FirstName = "Charlie", LastName = "Brown", DateOfBirth = new DateTime(1998, 7, 3), BankName = "Golden Gate Financial", BankAccount = 1200.90, BankMembership = BankMembership.Gold.GetDescription() }
            };

            using (var transaction = StateManager.CreateTransaction())
            {
                foreach (Client client in clients)
                    await _clientDictionary!.AddOrUpdateAsync(transaction, client.Id!.Value, client, (k, v) => v);

                await transaction.CommitAsync();
            }

            var clientsJson = new List<string>();

            using (var transaction = StateManager.CreateTransaction())
            {
                var enumerator = (await _clientDictionary!.CreateEnumerableAsync(transaction)).GetAsyncEnumerator();

                while (await enumerator.MoveNextAsync(CancellationToken.None))
                {
                    var client = enumerator.Current.Value;
                    clientsJson.Add(JsonConvert.SerializeObject(client));
                }
            }

            return clientsJson;
        }

        public async Task<string> EnlistMoneyTransfer(long? userSend, long? userReceive, double? amount)
        {
            using (var transaction = StateManager.CreateTransaction())
            {
                ConditionalValue<Client> clientToSend = await _clientDictionary!.TryGetValueAsync(transaction, userSend!.Value);
                ConditionalValue<Client> clientToReceive = await _clientDictionary!.TryGetValueAsync(transaction, userReceive!.Value);

                if (!clientToSend.HasValue || !clientToReceive.HasValue)
                {
                    throw new KeyNotFoundException($"Client with Id {userSend} or {userReceive} was not found.");
                }

                var clientToSendUpdate = clientToSend.Value;
                var clientToReceiveUpdate = clientToReceive.Value;

                clientToSendUpdate.BankAccount -= amount;
                clientToReceiveUpdate.BankAccount += amount;

                await _clientDictionary.TryUpdateAsync(transaction, userSend!.Value, clientToSendUpdate, clientToSend.Value);
                await _clientDictionary.TryUpdateAsync(transaction, userReceive!.Value, clientToReceiveUpdate, clientToReceive.Value);

                await transaction.CommitAsync();

                return string.Empty;
            }
        }

        #endregion

        #region ITransaction

        public Task<bool> Prepare()
        {
            throw new NotImplementedException();
        }

        public Task Commit()
        {
            throw new NotImplementedException();
        }

        public Task RollBack()
        {
            throw new NotImplementedException();
        }

        #endregion

        protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
        {
            return this.CreateServiceRemotingReplicaListeners();
        }
    }
}
