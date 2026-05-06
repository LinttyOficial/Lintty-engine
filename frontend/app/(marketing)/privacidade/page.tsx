import type { Metadata } from "next";

export const metadata: Metadata = {
  title: "Politica de Privacidade — Lintty",
  description:
    "Politica de Privacidade do Lintty: como tratamos dados coletados via landing page e canais de contato comercial, em conformidade com a LGPD.",
  alternates: { canonical: "https://lintty.com/privacidade" },
  robots: { index: true, follow: true },
};

export default function PrivacidadePage() {
  return (
    <main id="main" className="px-6 py-14">
      <article className="privacy-article mx-auto max-w-3xl">
        <h1>Politica de Privacidade &mdash; Lintty</h1>

        <p>
          <strong>Versao:</strong> 1.0 (Modo Projeto)
          <br />
          <strong>Data da ultima revisao:</strong> 28 de abril de 2026
          <br />
          <strong>Vigencia:</strong> a partir da publicacao na landing page{" "}
          <code>lintty.com</code>.
        </p>

        <blockquote>
          <strong>
            Modo atual: projeto pessoal em fase de validacao comercial.
          </strong>{" "}
          O Lintty e, neste momento, um projeto operado por pessoa natural
          (&ldquo;Operador&rdquo;), sem Pessoa Juridica formalizada. A constituicao
          de PJ esta prevista para o momento da assinatura do primeiro contrato
          de cliente. Esta Politica sera <strong>atualizada</strong> assim que a
          PJ for constituida (com inclusao de razao social, CNPJ e endereco
          fiscal).
        </blockquote>

        <hr />

        <h2>1. Quem somos</h2>
        <p>
          A presente Politica descreve como o <strong>Lintty</strong> &mdash;
          projeto de software operado por{" "}
          <strong>[INSERIR NOME COMPLETO DO OPERADOR]</strong>, pessoa natural,
          inscrita no CPF sob n&ordm; [INSERIR CPF], com endereco para contato
          em [INSERIR ENDERECO PARA CITACAO] &mdash; trata os dados pessoais
          coletados em seu site institucional (<code>lintty.com</code>) e
          canais de contato, em observancia a Lei Geral de Protecao de Dados
          Pessoais &mdash; LGPD (Lei n&ordm; 13.709/2018).
        </p>
        <p>
          O Lintty e um{" "}
          <strong>
            projeto de software de analise arquitetural automatizada
          </strong>{" "}
          para entregas de software .NET, em{" "}
          <strong>fase de validacao comercial pre-revenue</strong>. Esta
          Politica refere-se exclusivamente aos dados coletados via landing
          page e canais de contato comercial. As condicoes de tratamento de{" "}
          <strong>codigo fonte de clientes</strong>, quando e se o produto for
          ativado em piloto ou contrato, serao regidas por{" "}
          <strong>Acordo de Tratamento de Dados (DPA)</strong> especifico,
          firmado entre as Partes.
        </p>
        <p className="text-sm text-neutral-600">
          <strong>Nota sobre formalizacao:</strong> quando o projeto for
          constituido em Pessoa Juridica, esta secao sera substituida pela
          razao social, CNPJ e endereco da PJ. A Politica sera re-publicada
          com numeracao de versao atualizada e os titulares ativos serao
          comunicados por email.
        </p>

        <h2>2. Dados que coletamos</h2>
        <p>
          Coletamos exclusivamente os dados que voce nos fornece de forma ativa
          ao preencher um formulario de contato ou de solicitacao de
          demonstracao:
        </p>
        <table>
          <thead>
            <tr>
              <th>Dado</th>
              <th>Origem</th>
              <th>Finalidade</th>
            </tr>
          </thead>
          <tbody>
            <tr>
              <td>Nome</td>
              <td>Formulario de demo/contato</td>
              <td>Personalizacao da resposta comercial</td>
            </tr>
            <tr>
              <td>
                Email <strong>empresarial</strong>
              </td>
              <td>Formulario de demo/contato</td>
              <td>Canal de retorno para a conversa comercial</td>
            </tr>
            <tr>
              <td>Empresa</td>
              <td>Formulario de demo/contato</td>
              <td>Qualificacao do contato</td>
            </tr>
            <tr>
              <td>Mensagem (campo livre)</td>
              <td>Formulario de demo/contato</td>
              <td>Compreensao do interesse e contextualizacao da resposta</td>
            </tr>
          </tbody>
        </table>
        <p>
          Nao coletamos dados via cookies de rastreamento de terceiros (vide
          &sect;7). Nao solicitamos CPF, RG, telefone ou dados financeiros na
          landing page.
        </p>

        <h2>3. Finalidade do tratamento</h2>
        <p>
          O tratamento dos dados acima tem <strong>finalidade unica</strong>:{" "}
          <strong>
            contato comercial em resposta a sua solicitacao de demonstracao ou
            informacao
          </strong>
          .
        </p>
        <p>
          Nao enviamos newsletter, marketing recorrente, ou qualquer
          comunicacao fora do escopo da conversa comercial iniciada por voce,{" "}
          <strong>salvo se voce optar expressamente</strong> por receber tal
          comunicacao (opt-in explicito, por meio de aceite separado e
          inequivoco).
        </p>
        <p>
          <strong>Base legal (LGPD art. 7&ordm;):</strong> consentimento livre,
          informado e especifico (inciso I) e legitimo interesse na execucao
          de etapas pre-contratuais a pedido do titular (inciso V), conforme
          aplicavel a cada etapa.
        </p>

        <h2>4. Compartilhamento de dados</h2>
        <p>
          <strong>Nao compartilhamos, vendemos ou cedemos</strong> seus dados
          de contato a terceiros para finalidade de marketing.
        </p>
        <p>
          Os dados pessoais coletados via landing sao tratados internamente
          pela equipe comercial do Lintty.
        </p>
        <p>
          <strong>
            Sub-operadores que processam dados quando o produto e ativado
          </strong>{" "}
          (nao na landing &mdash; apenas quando voce se torna cliente e nos
          fornece codigo fonte para analise):
        </p>
        <table>
          <thead>
            <tr>
              <th>Sub-operador</th>
              <th>Funcao</th>
              <th>Localizacao</th>
              <th>Salvaguarda</th>
            </tr>
          </thead>
          <tbody>
            <tr>
              <td>
                <strong>Google Cloud Platform</strong>
              </td>
              <td>Hospedagem da landing e do email comercial</td>
              <td>
                EUA (<code>us-east1</code>)
              </td>
              <td>DPA padrao GCP, criptografia em transito e repouso</td>
            </tr>
          </tbody>
        </table>
        <p>
          <strong>Nota sobre a versao atual do produto (Sales Cut):</strong> a
          analise de codigo roda{" "}
          <strong>localmente no laptop do operador</strong>, sem envio a
          provedor de IA. Nao ha sub-operador de inferencia de modelo de
          linguagem na cadeia. Em versao futura (V1+), pretendemos adicionar
          Anthropic, PBC como sub-operador para analise semantica complementar,
          sob{" "}
          <strong>
            Zero Data Retention (ZDR) contratualizado
          </strong>
          . Esta pagina sera atualizada antes da ativacao desse sub-operador.
        </p>
        <p>
          A relacao completa de sub-operadores e respectivos termos e fornecida
          ao cliente no momento da contratacao, no DPA. Como cliente
          enterprise, voce pode solicitar atualizacao da lista a qualquer
          tempo.
        </p>

        <h2>5. Seus direitos como titular (LGPD art. 18)</h2>
        <p>Voce tem o direito de, a qualquer tempo, mediante requisicao:</p>
        <ul>
          <li>
            <strong>Confirmar</strong> a existencia de tratamento dos seus
            dados;
          </li>
          <li>
            <strong>Acessar</strong> os dados que mantemos sobre voce;
          </li>
          <li>
            <strong>Corrigir</strong> dados incompletos, inexatos ou
            desatualizados;
          </li>
          <li>
            <strong>Anonimizar, bloquear ou eliminar</strong> dados
            desnecessarios, excessivos ou tratados em desconformidade com a
            LGPD;
          </li>
          <li>
            <strong>Solicitar a portabilidade</strong> dos seus dados a outro
            fornecedor de servico, observados os segredos comercial e
            industrial;
          </li>
          <li>
            <strong>Eliminar</strong> os dados pessoais tratados com base no
            seu consentimento;
          </li>
          <li>
            <strong>Obter informacao</strong> sobre as entidades publicas e
            privadas com as quais compartilhamos seus dados;
          </li>
          <li>
            <strong>Obter informacao</strong> sobre a possibilidade de nao
            fornecer consentimento e suas consequencias;
          </li>
          <li>
            <strong>Revogar o consentimento</strong>, quando aplicavel.
          </li>
        </ul>
        <p>
          <strong>Canal para exercicio dos direitos:</strong>{" "}
          <a href="mailto:privacidade@lintty.com">privacidade@lintty.com</a>.
        </p>
        <p>
          <strong>Prazo de resposta:</strong> ate{" "}
          <strong>15 (quinze) dias</strong> corridos contados da sua
          requisicao.
        </p>
        <p>
          Para sua seguranca, podemos solicitar informacoes adicionais para
          confirmacao de identidade antes de atender a requisicao.
        </p>

        <h2>6. Retencao de dados</h2>
        <table>
          <thead>
            <tr>
              <th>Categoria</th>
              <th>Prazo de retencao</th>
              <th>Tratamento ao final</th>
            </tr>
          </thead>
          <tbody>
            <tr>
              <td>
                Leads de demo{" "}
                <strong>sem contato comercial efetivo</strong>
              </td>
              <td>12 meses contados da coleta</td>
              <td>Exclusao automatica</td>
            </tr>
            <tr>
              <td>Leads que viraram clientes</td>
              <td>Conforme DPA da relacao contratual</td>
              <td>Conforme DPA</td>
            </tr>
            <tr>
              <td>Logs de aplicacao (acessos ao site)</td>
              <td>90 dias</td>
              <td>Pseudo-anonimizacao e expurgo</td>
            </tr>
            <tr>
              <td>Comunicacoes por email</td>
              <td>
                Conforme politica de retencao interna do provedor de email + 24
                meses para fins de prestacao de contas comerciais
              </td>
              <td>Exclusao</td>
            </tr>
          </tbody>
        </table>
        <p>
          Voce pode solicitar a exclusao imediata dos seus dados a qualquer
          tempo via canal indicado em &sect;5.
        </p>

        <h2>7. Cookies e rastreamento</h2>
        <p>
          A landing page do Lintty{" "}
          <strong>
            nao utiliza cookies de rastreamento de terceiros
          </strong>{" "}
          (Google Analytics, Facebook Pixel, ferramentas de remarketing).
        </p>
        <p>
          [Caso utilize Plausible Analytics ou Umami: declarar aqui &mdash;
          &ldquo;Utilizamos a ferramenta de analytics Plausible/Umami, que
          opera sem cookies e sem coleta de dados pessoais identificaveis, em
          conformidade com LGPD/GDPR.&rdquo;]
        </p>
        <p>
          Cookies estritamente tecnicos podem ser utilizados para funcionamento
          da pagina (ex: preferencia de idioma) e nao armazenam dados
          pessoais.
        </p>

        <h2>8. Seguranca</h2>
        <p>
          Adotamos medidas tecnicas e organizacionais para proteger seus dados
          contra acessos nao autorizados, perda, alteracao ou destruicao,
          incluindo:
        </p>
        <ul>
          <li>Comunicacao criptografada (TLS 1.3);</li>
          <li>Principio do menor privilegio nos acessos internos;</li>
          <li>Autenticacao multifator obrigatoria para a equipe;</li>
          <li>Pseudo-anonimizacao em logs de aplicacao;</li>
          <li>Auditoria periodica de acessos.</li>
        </ul>
        <p>
          Apesar de todos os esforcos, nenhum sistema e absolutamente
          invulneravel. Em caso de incidente que envolva seus dados,
          comunicaremos voce e a Autoridade Nacional de Protecao de Dados
          (ANPD) nos prazos previstos na LGPD.
        </p>

        <h2>9. Encarregado pelo Tratamento de Dados (DPO)</h2>
        <p>Para os fins do art. 41 da LGPD, designamos como Encarregado:</p>
        <p>
          <strong>[INSERIR NOME DO ENCARREGADO]</strong> (no Modo Projeto, pode
          ser o proprio Operador ou DPO terceirizado).
          <br />
          <strong>Email:</strong>{" "}
          <a href="mailto:privacidade@lintty.com">privacidade@lintty.com</a>
        </p>
        <p className="text-sm text-neutral-600">
          <strong>Modo Projeto:</strong> com a constituicao da PJ, esta
          designacao sera revisada e oficializada conforme art. 41 da LGPD.
        </p>

        <h2>10. Alteracoes a esta Politica</h2>
        <p>
          Esta Politica pode ser atualizada periodicamente. Sempre que houver
          alteracao relevante, indicaremos a nova{" "}
          <strong>data da ultima revisao</strong> no topo do documento e,
          quando aplicavel, comunicaremos os titulares cujos dados estejam em
          tratamento ativo.
        </p>
        <p>
          <strong>Versao atual:</strong> 1.0
          <br />
          <strong>Data da ultima revisao:</strong> 27 de abril de 2026.
        </p>

        <h2>11. Contato</h2>
        <p>Duvidas sobre esta Politica ou sobre o tratamento dos seus dados:</p>
        <ul>
          <li>
            <strong>Privacidade e LGPD:</strong>{" "}
            <a href="mailto:privacidade@lintty.com">privacidade@lintty.com</a>
          </li>
          <li>
            <strong>Seguranca:</strong>{" "}
            <a href="mailto:seguranca@lintty.com">seguranca@lintty.com</a>
          </li>
          <li>
            <strong>Comercial:</strong> [INSERIR EMAIL COMERCIAL]
          </li>
        </ul>
      </article>
    </main>
  );
}
