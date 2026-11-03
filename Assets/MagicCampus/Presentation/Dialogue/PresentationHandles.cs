using System;
using MagicCampus.Contracts;

namespace MagicCampus.Presentation
{
    /// <summary>Presentation-specific failure codes that have no counterpart in ResultCodes.</summary>
    public static class PresentationCodes
    {
        public const string ClipFailed = "CLIP_FAILED";
    }

    internal static class PresentationHandles
    {
        internal static bool ValidId(string v)
        {
            if (string.IsNullOrEmpty(v)) return false;
            for (var i = 0; i < v.Length; i++)
                if (char.IsWhiteSpace(v[i]) || char.IsControl(v[i])) return false;
            return true;
        }

        internal static Diagnostic Diag(string code, string message, string expected, string actual)
        {
            return new Diagnostic(code, message, null, expected, actual);
        }

        /// <summary>Creates a handle that is already terminal Failed. Never reports success.</summary>
        internal static IHandleController<T> Failed<T>(IHandleFactory handles, string operationId, string code, string message, string expected, string actual)
        {
            var c = handles.Create<T>(operationId);
            c.Start();
            c.Fail(code, 0, new[] { Diag(code, message, expected, actual) });
            return c;
        }
    }
}
