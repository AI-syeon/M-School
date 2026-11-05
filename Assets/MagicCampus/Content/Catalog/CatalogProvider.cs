using System;

namespace MagicCampus.Content.Catalog
{
  // Holds the one compiled catalog of a running game.
  public sealed class CatalogProvider : ICatalogProvider
  {
    public CatalogProvider(ICatalog catalog)
    {
      Current = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public ICatalog Current { get; }
  }
}
