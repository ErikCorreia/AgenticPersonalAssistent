using System;

namespace Assistent;

class Program
{
    public static async Task Main(string[] args)
    {
        var app = new Myelin();
        await app.AskAsync(args);
    }
}