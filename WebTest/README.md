# Football VR — Painel de Controle

Este projeto permite acessar o painel administrativo do Football VR pelo computador e por dispositivos conectados à mesma rede Wi-Fi, como celulares e tablets.

O painel utiliza **Firebase Realtime Database** para se comunicar com o jogo Unity. Um servidor local em Node.js disponibiliza os arquivos do painel para os navegadores.

---

## 1. Estrutura dos arquivos

Mantenha os arquivos organizados na mesma pasta:

```text
FootballVR/
│
├── index.html       # Painel administrativo
├── server.js        # Servidor HTTP local
├── iniciar.bat      # Inicializador automático
└── README.md        # Este manual
```

**Importante:** não renomeie os arquivos sem atualizar as referências correspondentes no projeto.

## 2. Como funciona?

O sistema possui três partes principais:

### Painel administrativo — index.html

É a interface que o administrador utiliza pelo navegador.

Nele é possível acompanhar as informações da partida e executar comandos disponibilizados pelo painel.

O arquivo utiliza o Firebase Web SDK para se conectar ao banco de dados em tempo real.

### Servidor local — server.js

É responsável por disponibilizar o arquivo `index.html` e seus recursos através da rede local.

Ele utiliza Node.js e módulos nativos, portanto não precisa de instalação de dependências adicionais pelo npm.

O servidor utiliza a porta **5500** e aceita conexões pela rede local.

### Firebase Realtime Database

É responsável por armazenar e sincronizar as informações entre o painel e o jogo Unity.

O painel e o jogo consultam e atualizam os mesmos dados no Firebase.

O servidor Node.js não substitui o Firebase: ele apenas entrega os arquivos do painel ao navegador.

## 3. Instalação do Node.js

O computador que executará o painel precisa ter o Node.js instalado.

1. Acesse: https://nodejs.org/
2. Baixe a versão LTS.
3. Execute o instalador.
4. Mantenha as opções padrão durante a instalação.
5. Finalize e, se necessário, reinicie o computador.

Não é necessário instalar pacotes adicionais ou executar `npm install`.

## 4. Como iniciar o painel

Depois de instalar o Node.js:

1. Abra a pasta do Football VR.
2. Execute o arquivo `iniciar.bat`.
3. Aguarde a inicialização do servidor.
4. O navegador abrirá automaticamente o painel.

O BAT verifica se o Node.js está instalado, inicia o servidor e tenta identificar o endereço IP local do computador.

Uma janela adicional será aberta para manter o servidor funcionando.

**Não feche a janela do servidor enquanto estiver utilizando o painel.**

Para encerrar o servidor, pressione `CTRL + C` na janela correspondente.

## 5. Acessando pelo computador

O painel poderá ser acessado pelo próprio computador através do endereço:

```text
http://localhost:5500/
```

Também poderá ser acessado pelo endereço IP local, por exemplo:

```text
http://192.168.1.100:5500/
```

O IP apresentado será diferente em cada rede.

## 6. Acessando pelo celular ou tablet

Para acessar o painel por outro dispositivo:

1. Conecte o computador e o celular à mesma rede Wi-Fi.
2. Inicie o servidor pelo `iniciar.bat`.
3. Abra o painel utilizando o endereço IP local do computador.
4. Localize o QR Code no painel.
5. Leia o QR Code utilizando a câmera do celular.

O navegador do celular abrirá automaticamente o endereço do painel.

**Atenção:** o QR Code utiliza o endereço da página aberta no navegador.

Se o computador estiver acessando pelo endereço `localhost`, o QR Code também poderá conter `localhost`, o que não funcionará no celular.

Por isso, para compartilhar o acesso, utilize o endereço IP local do computador.

## 7. Como funciona a comunicação com o Unity?

A comunicação acontece através do Firebase Realtime Database.

O fluxo básico é:

```text
       PAINEL ADMINISTRATIVO
             index.html
                 |
                 |
                 v
        FIREBASE REALTIME DB
                 ^
                 |
                 |
             JOGO UNITY
```

O painel e o Unity trabalham com os mesmos dados armazenados no Firebase.

A sala utilizada atualmente é:

```text
ARENA01
```

Os dados da partida ficam organizados dentro do caminho:

```text
Rooms/
  ARENA01/
    Match/
    Score/
    Players/
    Actions/
    ProcessedActions/
```

### Principais informações

- **Match:** informações e estado da partida.
- **Score:** placar.
- **Players:** informações dos jogadores.
- **Actions:** comandos e ações enviados ao jogo.
- **ProcessedActions:** registro de ações já processadas.

O Unity recebe as atualizações e executa os comportamentos correspondentes.

O painel também recebe atualizações do banco, permitindo acompanhar as informações sem precisar atualizar manualmente a página.

## 8. Comando de reiniciar partida

O painel utiliza o campo `RestartSignal` dentro de:

```text
Rooms/ARENA01/Match/RestartSignal
```

Quando o administrador solicita o reinício, o painel atualiza esse sinal no Firebase.

O Unity verifica a alteração e executa o procedimento de reinicialização.

O jogo mantém o controle do último sinal processado para evitar que o mesmo comando seja executado repetidamente.

## 9. Requisitos de funcionamento

Para utilizar o painel corretamente, são necessários:

- Computador com Windows.
- Node.js instalado.
- Arquivos do painel na mesma pasta.
- Conexão com a internet.
- Firebase configurado corretamente.
- Celular e computador na mesma rede Wi-Fi, quando o acesso for externo.
- Firewall permitindo conexões na porta TCP 5500.

**Importante:** o servidor é local, mas o Firebase depende de internet. Portanto, estar conectado ao Wi-Fi não significa necessariamente que o Firebase estará acessível.

## 10. Solução de problemas

### O arquivo iniciar.bat não funciona

Verifique se o Node.js está instalado.

Abra o Prompt de Comando e execute:

node -v

Se aparecer uma versão, como `v22.x.x`, o Node.js está disponível.

Se aparecer que o comando não foi encontrado, reinstale o Node.js e verifique se ele foi adicionado ao PATH.

Link node.js:
https://nodejs.org/en/download

### O servidor informa que a porta está em uso

Isso significa que outro programa já está utilizando a porta 5500.

Feche outras instâncias do servidor Football VR ou identifique o programa que está utilizando essa porta.

### O computador abre o painel, mas o celular não

Verifique:

- Se os dois dispositivos estão na mesma rede.
- Se o endereço utilizado no celular é o IP local do computador.
- Se o Firewall do Windows permite conexões de entrada na porta TCP 5500.
- Se o roteador não possui isolamento entre dispositivos Wi-Fi.

Redes de convidados podem bloquear a comunicação entre dispositivos.

### O painel abre, mas não conecta ao Firebase

Verifique:

- Se o computador possui acesso à internet.
- Se o Firebase Realtime Database está disponível.
- Se a autenticação anônima está habilitada no Firebase Authentication.
- Se as regras do banco permitem as operações necessárias.
- Se a configuração do Firebase no `index.html` está correta.

### O QR Code não aparece

Verifique se o computador possui conexão com a internet.

A biblioteca utilizada para gerar o QR Code é carregada por uma CDN externa. Sem acesso a essa biblioteca, a geração poderá falhar.

## 11. Segurança

O servidor foi desenvolvido para disponibilizar o painel em uma rede local confiável.

Algumas recomendações:

- Não coloque senhas, chaves privadas ou arquivos confidenciais nessa pasta.
- Não exponha a porta 5500 diretamente à internet.
- Utilize uma rede Wi-Fi confiável.
- Configure corretamente as regras de segurança do Firebase.
- Não considere a autenticação anônima uma proteção suficiente para operações administrativas.

O servidor utiliza HTTP, sem criptografia TLS. Por isso, não deve ser utilizado como servidor público para acesso pela internet.

## 12. Migração do Firebase para a conta do cliente

Atualmente, o Firebase utilizado pelo Football VR pertence à conta de desenvolvimento.

Para entregar o sistema utilizando a infraestrutura do cliente, será necessário criar um novo projeto Firebase na conta do próprio cliente e configurar o painel e o jogo para utilizarem esse novo ambiente.

Essa etapa garante que o banco de dados, os jogadores e as informações das partidas fiquem sob responsabilidade do cliente.

### Etapas de configuração

**1. Criar ou acessar a conta Firebase do cliente**

Acesse https://firebase.google.com/ e entre com a conta Google que será responsável pelo projeto.

**2. Criar um novo projeto Firebase**

Crie um projeto com o nome desejado pelo cliente.

**3. Configurar o Firebase Realtime Database**

Dentro do projeto:

- Acesse Realtime Database.
- Crie um banco de dados.
- Escolha a região desejada.
- Configure as regras de segurança necessárias para o funcionamento do jogo. (Muita atencao, dependendo da regra, precisa alterar a comuicacao com o jogo)

**4. Criar o aplicativo Web e Unity no Firebase**

Nas configurações do projeto, adicione um aplicativo Web.
Nas configurações do projeto, adicione um aplicativo Unity.

Copie os dados de configuração do Firebase Web SDK. Eles serão utilizados pelo painel `index.html`.

**5. Configurar o painel administrativo**

Abra o arquivo `index.html` e substitua os dados de configuração do Firebase pelos dados do novo projeto.

Verifique principalmente:

- `apiKey`
- `authDomain`
- `databaseURL`
- `projectId`
- `appId`

**6. Configurar o projeto Unity**

Atualize as configurações do Firebase no projeto Unity para apontar para o mesmo projeto criado na conta do cliente.

Se o projeto utilizar arquivos de configuração específicos para Android ou outras plataformas, substitua-os pelos arquivos correspondentes ao novo projeto.

**7. Revisar autenticação e regras de segurança**

Habilite os métodos de autenticação necessários, incluindo autenticação anônima caso ela continue sendo utilizada.

Configure as regras do Realtime Database para permitir somente as operações necessárias ao funcionamento do jogo e do painel.

**8. Gerar uma nova build**

Após atualizar as configurações, gere uma nova build do jogo Unity.

A build anterior continuará utilizando as configurações antigas.

### Resultado esperado

Ao finalizar a migração, o painel administrativo e o jogo Unity estarão conectados ao Firebase do cliente.

O cliente será responsável pelo projeto Firebase, pelo banco de dados e pelas configurações de acesso.

---

## 13. Resumo da utilização

Para utilizar o sistema no dia a dia:

1. Ligue o computador.
2. Conecte-o à internet.
3. Conecte o celular à mesma rede Wi-Fi, caso necessário.
4. Abra a pasta Football VR.
5. Execute `iniciar.bat`.
6. Aguarde o painel abrir.
7. Utilize o QR Code para conectar outros dispositivos.
8. Mantenha a janela do servidor aberta durante a partida.
9. O projeto necessida de 2 jogadores online
10. Em um Computador, abre o jogo Player 1
11. No outro Computador, abre o jogo Player 2
12. O operador tem o controle dos jogos pelo admin, podendo reiniciar a partida quando quiser.

---

**Football VR — Painel Administrativo**

Servidor local desenvolvido em Node.js.  
Comunicação em tempo real através do Firebase Realtime Database.  
Sala configurada: `ARENA01`.