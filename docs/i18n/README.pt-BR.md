# ComCross

> Aviso de documentação: este documento pode ter sido gerado ou traduzido com
> ajuda de IA. Ele serve como referência de acessibilidade e orientação. Se
> houver conflito com o README em inglês ou com especificações formais, use o
> texto-fonte em inglês como autoridade.

ComCross é uma caixa de ferramentas de comunicação embarcada e multiplataforma
para fluxos seriais, TCP e UDP.

## Estado

ComCross ainda está em fase pré-estável. Mudanças incompatíveis são permitidas
quando corrigem limites de arquitetura, diretórios de execução, layout de
plugins ou contratos de empacotamento. Essas mudanças devem ser documentadas.

## Capacidades principais

- Adaptadores seriais, TCP e UDP entregues como plugins isolados.
- Gerenciamento de sessões e workloads com descritores persistidos.
- Fluxo RX/TX pesquisável com atributos de frame limitados.
- Páginas de configuração de plugins e estado de UI produzido por plugins.
- Localização integrada em inglês e chinês simplificado para o Shell.

## Início rápido para desenvolvimento

```bash
dotnet restore
dotnet build ComCross.sln --no-restore
dotnet run --project src/Shell/ComCross.Shell.csproj
dotnet test ComCross.sln --no-build
```

## Observações importantes

- O futuro ponto de entrada para usuários está planejado como `ComCross.Startup`.
- Versões públicas para Windows podem usar inicialmente um certificado de teste
  autoassinado. O Windows não confia nesse certificado por padrão.
- Antes de ignorar avisos do sistema operacional, verifique checksums e a
  assinatura da release.
- A assinatura de pacotes oficiais de plugins será separada da assinatura de
  código do Windows e da assinatura dos artefatos de release.

## Referências

- README raiz: [../../README.md](../../README.md)
- Índice de especificações: [../specs/00-Index.md](../specs/00-Index.md)
- Design de Startup, identidade de instância e assinatura:
  [../specs/12-Startup-Identity-Signing-Design.md](../specs/12-Startup-Identity-Signing-Design.md)
