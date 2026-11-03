using MagicCampus.Contracts;

namespace MagicCampus.Presentation.Dialogue
{
    public enum DialogueWindowKind { None, Say, Choose }

    public enum NavDirection { Up, Down, Left, Right }

    /// <summary>Deterministic UI command entry points. Hardware polling and pointer input use exactly these.</summary>
    public interface IDialogueUiCommands
    {
        bool Navigate(NavDirection direction);
        bool Confirm();
        bool Cancel();
    }

    /// <summary>Presentation-only look settings, copied from the component's serialized fields every OnGUI.</summary>
    public sealed class DialogueGuiSettings
    {
        public float TextScale = 1f;
        public int BaseFontSize = 30;
        public float ReferenceHeight = 1080f;
        public float PanelHeightRatio = 0.34f;
        public string[] FontNames = new string[0];
        public string ConfirmHint = string.Empty;
        public string ScrollHint = string.Empty;
        public string ChoiceHint = string.Empty;
    }

    internal sealed class DialogueWindow
    {
        public DialogueWindowKind Kind;
        public string Owner;
        public string Speaker = string.Empty;
        public string Text = string.Empty;
        public string Prompt = string.Empty;
        public ChoiceOption[] Options = new ChoiceOption[0];
        public int Focus;
        public int ScrollLines;
        public float OptionScrollPx;
        public int DrawnFocus = -1;
        public int OpenFrame;
        public PauseToken Token;
        public bool HasSavedFocus;
        public object SavedFocus;
        public bool Closed;
        public IHandleController<bool> SayController;
        public IHandleController<ChoiceSelection> ChoiceController;
    }
}
