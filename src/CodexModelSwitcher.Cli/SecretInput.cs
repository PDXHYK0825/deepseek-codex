namespace CodexModelSwitcher.Cli;

internal static class SecretInput
{
    public static string Read(string prompt)
    {
        Console.Error.Write(prompt);
        if (Console.IsInputRedirected)
        {
            var redirected = Console.In.ReadLine() ?? string.Empty;
            return redirected.Trim();
        }

        var characters = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.Error.WriteLine();
                return new string(characters.ToArray()).Trim();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (characters.Count > 0)
                {
                    characters.RemoveAt(characters.Count - 1);
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                characters.Add(key.KeyChar);
            }
        }
    }
}
