namespace CodexModelSwitcher.Abstractions;

public interface ISecretStore
{
    bool Contains(string key);

    string? Read(string key);

    void Write(string key, string value);

    bool Delete(string key);
}
