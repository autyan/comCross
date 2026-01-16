using Microsoft.Extensions.Logging;
using ComCross.Core.Cpdl.Ast;
using ComCross.Shared.Interfaces;

namespace ComCross.Core.Cpdl.Compiler;

/// <summary>
/// CPDL编译器 - 将CPDL协议定义编译为可执行的IMessageParser
/// </summary>
public sealed class CpdlCompiler
{
    private readonly ILogger<CpdlCompiler> _logger;
    private readonly ILogger<CpdlMessageParser> _parserLogger;

    public CpdlCompiler(ILogger<CpdlCompiler> logger, ILogger<CpdlMessageParser> parserLogger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _parserLogger = parserLogger ?? throw new ArgumentNullException(nameof(parserLogger));
    }

    /// <summary>
    /// 编译CPDL协议定义
    /// </summary>
    public CompilationResult Compile(string source)
    {
        try
        {
            _logger.LogInformation("Starting CPDL compilation");

            // 1. 词法分析
            var lexer = new CpdlLexer(source);
            var lexerResult = lexer.ScanTokens();
            
            if (!lexerResult.Success)
            {
                return CompilationResult.Failure(
                    $"Lexer errors: {string.Join(", ", lexerResult.Errors)}");
            }

            // 2. 语法分析
            var parser = new CpdlParser(lexerResult.Tokens.ToList());
            var parseResult = parser.Parse();
            
            if (!parseResult.Success)
            {
                return CompilationResult.Failure(
                    $"Parser errors: {string.Join(", ", parseResult.Errors)}");
            }

            // 3. 语义分析
            var semanticAnalyzer = new SemanticAnalyzer();
            var semanticResult = semanticAnalyzer.Analyze(parseResult.Protocol!);
            
            if (!semanticResult.Success)
            {
                return CompilationResult.Failure(
                    $"Semantic errors: {string.Join(", ", semanticResult.Errors)}");
            }

            // 4. 代码生成（目前只支持单个消息）
            if (parseResult.Protocol!.Messages.Count == 0)
            {
                return CompilationResult.Failure("Protocol must have at least one message");
            }

            var message = parseResult.Protocol.Messages[0];
            var codeGen = new CodeGenerator(message);
            var parseFunc = codeGen.GenerateParser();

            // 5. 创建Parser实例
            var messageParser = new CpdlMessageParser(
                parseResult.Protocol,
                parseFunc,
                _parserLogger
            );

            _logger.LogInformation("CPDL compilation successful");
            return CompilationResult.Success(messageParser);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CPDL compilation failed");
            return CompilationResult.Failure($"Compilation error: {ex.Message}");
        }
    }

    /// <summary>
    /// 编译CPDL协议定义（从AST）
    /// </summary>
    public CompilationResult Compile(ProtocolDefinition protocol)
    {
        try
        {
            // 语义分析
            var semanticAnalyzer = new SemanticAnalyzer();
            var semanticResult = semanticAnalyzer.Analyze(protocol);
            
            if (!semanticResult.Success)
            {
                return CompilationResult.Failure(
                    $"Semantic errors: {string.Join(", ", semanticResult.Errors)}");
            }

            // 代码生成
            if (protocol.Messages.Count == 0)
            {
                return CompilationResult.Failure("Protocol must have at least one message");
            }

            var message = protocol.Messages[0];
            var codeGen = new CodeGenerator(message);
            var parseFunc = codeGen.GenerateParser();

            // 创建Parser
            var messageParser = new CpdlMessageParser(protocol, parseFunc, _parserLogger);

            return CompilationResult.Success(messageParser);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CPDL compilation failed");
            return CompilationResult.Failure($"Compilation error: {ex.Message}");
        }
    }
}

/// <summary>
/// 编译结果
/// </summary>
public sealed class CompilationResult
{
    public bool IsSuccess { get; init; }
    public IMessageParser? Parser { get; init; }
    public string? ErrorMessage { get; init; }

    public static CompilationResult Success(IMessageParser parser)
    {
        return new CompilationResult
        {
            IsSuccess = true,
            Parser = parser
        };
    }

    public static CompilationResult Failure(string errorMessage)
    {
        return new CompilationResult
        {
            IsSuccess = false,
            ErrorMessage = errorMessage
        };
    }
}
