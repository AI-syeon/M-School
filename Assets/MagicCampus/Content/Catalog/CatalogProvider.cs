using System;
using MagicCampus.Contracts;

namespace MagicCampus.Content.Catalog
{
  // Holds the last valid catalog. A bundle that fails compilation is rejected whole and Current is untouched.
  public sealed class CatalogProvider : ICatalogProvider
  {
    readonly ICatalogCompiler compiler;
    readonly object gate = new object();
    ICatalog current;

    public CatalogProvider(ICatalogCompiler compiler) : this(compiler, null) { }

    public CatalogProvider(ICatalogCompiler compiler, ICatalog initial)
    {
      this.compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
      current = initial;
    }

    public ICatalog Current { get { lock (gate) { return current; } } }

    public Outcome<ICatalog> TryReplace(ContentBundle catalog, ContentBundle runner, ContentBundle functions, ContentBundle catalogSchema, ContentBundle runnerSchema)
    {
      Outcome<ICatalog> o;
      try { o = compiler.Compile(catalog, runner, functions, catalogSchema, runnerSchema); }
      catch (Exception ex)
      {
        return Outcome<ICatalog>.Fail(ResultCodes.ContentInvalid, 0, Diags.Make(ResultCodes.ContentInvalid, "", "", "", "", 0, "compiler threw " + ex.GetType().Name + ": " + ex.Message, null, null));
      }
      if (o == null)
        return Outcome<ICatalog>.Fail(ResultCodes.ContentInvalid, 0, Diags.Make(ResultCodes.ContentInvalid, "", "", "", "", 0, "compiler returned no outcome", null, null));
      if (o.Succeeded) lock (gate) { current = o.Value; }
      return o;
    }
  }
}
