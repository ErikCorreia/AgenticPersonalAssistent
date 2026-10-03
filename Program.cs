using System;

namespace Assistent;

class Program
{
    public static async Task Main(string[] args)
    {
        var app = new Myelin();
        var firstInput = args.Length > 0 ? string.Join(' ', args) : null;

        Console.WriteLine("Myelin pronto. Digite sua mensagem (ou 'sair' para encerrar).");

        while (true)
        {
            Console.Write("Você: ");
            var input = firstInput ?? Console.ReadLine();
            firstInput = null;

            if (input is null)
            {
                break;
            }

            input = input.Trim();
            if (input.Equals("sair", StringComparison.OrdinalIgnoreCase)
                || input.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (input.Equals("/falar", StringComparison.OrdinalIgnoreCase))
            {
                app.SpeakResponses = !app.SpeakResponses;
                Console.WriteLine($"Respostas faladas: {(app.SpeakResponses ? "ligadas" : "desligadas")}.");
                continue;
            }

            if (input.Equals("/voz", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var heard = await app.ListenAsync();
                    if (heard is null)
                    {
                        Console.WriteLine("Não entendi nada. Tente novamente.");
                        continue;
                    }
                    Console.WriteLine($"Você (voz): {heard}");
                    input = heard;
                }
                catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException or DllNotFoundException or NAudio.MmException)
                {
                    Console.WriteLine($"Reconhecimento de voz indisponível: {exception.Message}");
                    continue;
                }
            }

            if (input.Length == 0)
            {
                continue;
            }

            try
            {
                var response = await app.AskAsync(input);
                Console.WriteLine($"Myelin: {response}");
                if (app.SpeakResponses) await app.SpeakAsync(response);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("A solicitação foi cancelada.");
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or InvalidDataException or System.Text.Json.JsonException)
            {
                Console.WriteLine($"Não foi possível concluir a solicitação: {exception.Message}");
            }
        }

        Console.WriteLine("Até logo!");
    }
}