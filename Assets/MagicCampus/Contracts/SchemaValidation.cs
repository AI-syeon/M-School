using System.Collections.Generic;

namespace MagicCampus.Contracts
{
    // Task1 contract addition found in Task3 preparation: standards validation stays behind an IO adapter.
    public interface ISchemaValidationPort
    {
        IReadOnlyList<Diagnostic> Validate(DataNode schema, DataNode instance, string file);
    }
}
