using System;
using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.Operations
{
    /// <summary>
    /// Closed registry of domain operation ports. Only the domains of the perform enum can be registered, each at most once.
    /// A domain without a registered port is reported as null so the runner refuses with UNREGISTERED; nothing here fakes gameplay.
    /// </summary>
    public sealed class DomainOperationRegistry : IDomainOperationRegistry
    {
        private static readonly string[] Closed = { "class", "encounter", "puzzle", "craft", "relationship", "project", "activity" };
        private readonly SortedDictionary<string, IDomainOperationPort> byDomain = new SortedDictionary<string, IDomainOperationPort>(StringComparer.Ordinal);

        public DomainOperationRegistry(IEnumerable<IDomainOperationPort> ports)
        {
            if (ports == null) throw new ArgumentNullException(nameof(ports));
            foreach (var port in ports)
            {
                if (port == null) throw new ArgumentException("null port", nameof(ports));
                if (port.Domain == null || Array.IndexOf(Closed, port.Domain) < 0)
                    throw new ArgumentException("domain is not in the closed set: " + port.Domain, nameof(ports));
                if (byDomain.ContainsKey(port.Domain))
                    throw new ArgumentException("duplicate domain: " + port.Domain, nameof(ports));
                byDomain.Add(port.Domain, port);
            }
        }

        public static DomainOperationRegistry Empty => new DomainOperationRegistry(new IDomainOperationPort[0]);

        public static IReadOnlyList<string> KnownDomains => Array.AsReadOnly((string[])Closed.Clone());

        public IReadOnlyList<string> Domains => new List<string>(byDomain.Keys);

        public IDomainOperationPort Find(string domain)
        {
            IDomainOperationPort port;
            return domain != null && byDomain.TryGetValue(domain, out port) ? port : null;
        }
    }
}
