# Segurança da Informação e Gestão de Acessos — Aurora Log

## Autenticação

Todo acesso a sistemas corporativos exige autenticação multifator. O segundo fator aceito é aplicativo autenticador ou chave física; SMS foi descontinuado como segundo fator em janeiro de 2025.

Senhas têm mínimo de catorze caracteres e não expiram por tempo — a troca só é exigida quando há indício de comprometimento. Essa mudança seguiu a recomendação de deixar de forçar rotação periódica, que na prática levava a senhas mais fracas.

## Concessão de acesso

O acesso é concedido pelo princípio do menor privilégio: o colaborador recebe o mínimo necessário para a função, e nada além. Toda concessão tem um responsável nomeado e é revista a cada seis meses.

Acesso a ambiente de produção exige aprovação dupla — gestor da área e time de segurança — e é temporário por padrão, com validade de oito horas.

## Dados de cliente

Dados de cliente não podem ser copiados para máquinas locais nem para ambientes de desenvolvimento. Quando é necessário reproduzir um problema com dados reais, usa-se o ambiente de depuração isolado, com registro de acesso e expiração automática em 24 horas.

## Incidentes

Suspeita de incidente deve ser comunicada ao time de segurança imediatamente, pelo canal dedicado, mesmo sem confirmação. Comunicar cedo e errar é explicitamente preferível a esperar para ter certeza.

## Dispositivos

Notebooks corporativos têm disco criptografado e bloqueio automático em cinco minutos. Dispositivos pessoais só acessam e-mail e mensageria, nunca código-fonte ou dados de cliente.
