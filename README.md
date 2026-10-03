# Myelin

Assistente pessoal para Windows, executado no console e conectado ao Gemini ou Ollama. As ferramentas são carregadas como plugins .NET permitidos na configuração.

## Executar

Requer o .NET 10 SDK.

```powershell
dotnet restore Assistent.sln
dotnet build Assistent.sln
dotnet run --project Assistent.csproj
```

Configure `GOOGLE_AI_API_KEY` no ambiente do processo ou informe a chave quando solicitada. A solicitação interativa não mostra o texto digitado, e a chave não é gravada em `appsettings.json`. Evite colocar segredos em comandos salvos no histórico do terminal.

O modelo e as opções não secretas ficam em `appsettings.json`. A chave é enviada ao Gemini pelo cabeçalho de autenticação, nunca na URL. Quando Gemini está selecionado, as mensagens da conversa são processadas pelo serviço na nuvem.

## Escolher o provedor de IA

Edite `Assistent.Provider` em `appsettings.json` para `Gemini` ou `Ollama`. Gemini permanece como padrão. Para Ollama local, instale e inicie o Ollama para Windows, baixe um modelo que suporte chamadas de ferramentas e configure, por exemplo:

```json
"Assistent": {
  "Provider": "Ollama",
  "OllamaBaseUrl": "http://localhost:11434",
  "OllamaModel": "qwen3:8b"
}
```

Baixe o modelo configurado no Ollama antes de iniciar o assistente, por exemplo `ollama pull qwen3:8b`. Os nomes de modelo aceitos dependem dos modelos instalados e da compatibilidade com tool calling. `OLLAMA_BASE_URL`, `OLLAMA_MODEL` e `ASSISTENT_PROVIDER` podem substituir as opções de configuração. Com Ollama ativo, conversa e chamadas de ferramentas são enviadas ao endpoint local `/api/chat`, sem usar a API Gemini nem solicitar sua chave. Confirme que a URL aponta para uma instância confiável.

## Privacidade e autorização

- O conteúdo de um arquivo só é lido depois de confirmação; a confirmação informa se o conteúdo será enviado ao Gemini ou processado pela instância Ollama configurada. Listagens e buscas também pedem consentimento, pois metadados locais são enviados ao provedor selecionado.
- `system_info` consulta o espaço total/usado/disponível nas unidades prontas e o uso de memória RAM. Como esses dados são enviados ao provedor de IA, a consulta pede consentimento.
- Escrita, exclusão e criação de pastas exigem confirmação. A exclusão de pastas é recursiva.
- O gerenciador permite escrita somente nas raízes graváveis; por padrão, a pasta de perfil do usuário. Pastas críticas do Windows são permitidas para leitura e protegidas contra escrita mesmo se houver uma raiz gravável que as contenha.
- `ASSISTENT_ALLOWED_FILE_ROOTS` define raízes graváveis separadas por `;` no Windows. `ASSISTENT_READ_ONLY_ROOTS` acrescenta raízes somente de leitura; `C:\Windows`, `Program Files`, `Program Files (x86)` e `ProgramData` são somente leitura por padrão. A raiz da unidade pode ser listada, sem pesquisa recursiva ou leitura de arquivos.
- `list_recycle_bin` consulta os itens da Lixeira do Windows após consentimento. `empty_recycle_bin` esvazia a Lixeira de todos os volumes e exige digitar exatamente `ESVAZIAR LIXEIRA`; essa remoção é permanente.
- Leitura e escrita de arquivo têm limite de 1 MB. Buscas ignoram arquivos ocultos, arquivos de sistema e pontos de nova análise, e limitam a profundidade a 8 níveis.

## Plugins

`Assistent:AllowedPlugins` em `appsettings.json` limita quais DLLs no diretório de plugins podem ser carregadas. `ASSISTENT_ALLOWED_PLUGINS` pode substituir a lista, com nomes separados por `;`. O padrão permite `WebSearchToolkit.dll`, `ExampleToolkit.dll` e `FileManagerToolkit.dll`.

Plugins são código executável no processo do assistente; a lista de nomes não os isola nem substitui a confiança no código e na pasta de plugins. Permita apenas DLLs de origem confiável.

## Testes

```powershell
dotnet test Assistent.sln
```

## Pesquisa na web (web_search)

O plugin web_search usa a Brave Search API (search) e busca texto de páginas públicas (etch). Defina a chave em BRAVE_API_KEY (variável de ambiente; nunca em arquivos). Toda ação exige aprovação, pois envia dados a terceiros. O etch aceita só http/https, bloqueia hosts locais/privados (inclusive via redirecionamento e DNS rebinding), limita o download a 1 MB e só lê HTML/texto.

## Guardrails

Todo fluxo passa por um GuardrailPipeline (App/Core/Guardrails): a entrada do usuário e a saída do LLM são higienizadas (caracteres de controle/invisíveis, redação de segredos, limites de tamanho); chamadas de ferramenta são validadas (nome, tamanho, repetição); resultados de ferramentas são truncados, redigidos e embrulhados como dados não confiáveis, com detecção de prompt injection que interrompe novas chamadas. Limites em appsettings.json (seção Guardrails).
