namespace CSharp.Delegates;

public interface IDemo
{
    string Title { get; }

    /// <summary>Типичный вопрос на собеседовании, на который отвечает демо.</summary>
    string Question { get; }

    Task RunAsync();
}
