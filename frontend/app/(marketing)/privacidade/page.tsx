import type { Metadata } from "next";
import { AuroraPageHeader } from "@/components/HeroAurora";

export const metadata: Metadata = {
  title: "Política de Privacidade — Lintty",
  description:
    "Política de Privacidade do Lintty: como tratamos dados coletados via landing page e canais de contato comercial, em conformidade com a LGPD.",
  alternates: { canonical: "https://lintty.com/privacidade" },
  robots: { index: true, follow: true },
};

export default function PrivacidadePage() {
  return (
    <>
      <AuroraPageHeader
        eyebrow="Compliance · LGPD"
        title="Política de Privacidade — Lintty"
        subtitle="Como tratamos os dados coletados via landing page e canais de contato comercial, em conformidade com a LGPD."
      />
      <main id="main" className="lt-dark-glow lt-noise px-6 py-14 text-paper">
        <article className="privacy-article mx-auto max-w-3xl">
          <p>
          <strong>Versão:</strong> 1.0 (Modo Projeto)
          <br />
          <strong>Data da última revisão:</strong> 28 de abril de 2026
          <br />
          <strong>Vigência:</strong> a partir da publicação na landing page{" "}
          <code>lintty.com</code>.
        </p>

        <blockquote>
          <strong>
            Modo atual: projeto pessoal em fase de validação comercial.
          </strong>{" "}
          O Lintty é, neste momento, um projeto operado por pessoa natural
          (&ldquo;Operador&rdquo;), sem Pessoa Jurídica formalizada. A constituição
          de PJ está prevista para o momento da assinatura do primeiro contrato
          de cliente. Esta Política será <strong>atualizada</strong> assim que a
          PJ for constituída (com inclusão de razão social, CNPJ e endereço
          fiscal).
        </blockquote>

        <hr />

        <h2>1. Quem somos</h2>
        <p>
          A presente Política descreve como o <strong>Lintty</strong> &mdash;
          projeto de software operado por{" "}
          <strong>[INSERIR NOME COMPLETO DO OPERADOR]</strong>, pessoa natural,
          inscrita no CPF sob n&ordm; [INSERIR CPF], com endereço para contato
          em [INSERIR ENDEREÇO PARA CITAÇÃO] &mdash; trata os dados pessoais
          coletados em seu site institucional (<code>lintty.com</code>) e
          canais de contato, em observância à Lei Geral de Proteção de Dados
          Pessoais &mdash; LGPD (Lei n&ordm; 13.709/2018).
        </p>
        <p>
          O Lintty é um{" "}
          <strong>
            projeto de software de análise arquitetural automatizada
          </strong>{" "}
          para entregas de software .NET, em{" "}
          <strong>fase de validação comercial pré-revenue</strong>. Esta
          Política refere-se exclusivamente aos dados coletados via landing
          page e canais de contato comercial. As condições de tratamento de{" "}
          <strong>código-fonte de clientes</strong>, quando e se o produto for
          ativado em piloto ou contrato, serão regidas por{" "}
          <strong>Acordo de Tratamento de Dados (DPA)</strong> específico,
          firmado entre as Partes.
        </p>
        <p className="text-sm text-neutral-400">
          <strong>Nota sobre formalização:</strong> quando o projeto for
          constituído em Pessoa Jurídica, esta seção será substituída pela
          razão social, CNPJ e endereço da PJ. A Política será re-publicada
          com numeração de versão atualizada e os titulares ativos serão
          comunicados por email.
        </p>

        <h2>2. Dados que coletamos</h2>
        <p>
          Coletamos exclusivamente os dados que você nos fornece de forma ativa
          ao preencher um formulário de contato ou de solicitação de
          demonstração:
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
              <td>Formulário de demo/contato</td>
              <td>Personalização da resposta comercial</td>
            </tr>
            <tr>
              <td>
                Email <strong>empresarial</strong>
              </td>
              <td>Formulário de demo/contato</td>
              <td>Canal de retorno para a conversa comercial</td>
            </tr>
            <tr>
              <td>Empresa</td>
              <td>Formulário de demo/contato</td>
              <td>Qualificação do contato</td>
            </tr>
            <tr>
              <td>Mensagem (campo livre)</td>
              <td>Formulário de demo/contato</td>
              <td>Compreensão do interesse e contextualização da resposta</td>
            </tr>
          </tbody>
        </table>
        <p>
          Não coletamos dados via cookies de rastreamento de terceiros (vide
          &sect;7). Não solicitamos CPF, RG, telefone ou dados financeiros na
          landing page.
        </p>

        <h2>3. Finalidade do tratamento</h2>
        <p>
          O tratamento dos dados acima tem <strong>finalidade única</strong>:{" "}
          <strong>
            contato comercial em resposta à sua solicitação de demonstração ou
            informação
          </strong>
          .
        </p>
        <p>
          Não enviamos newsletter, marketing recorrente, ou qualquer
          comunicação fora do escopo da conversa comercial iniciada por você,{" "}
          <strong>salvo se você optar expressamente</strong> por receber tal
          comunicação (opt-in explícito, por meio de aceite separado e
          inequívoco).
        </p>
        <p>
          <strong>Base legal (LGPD art. 7&ordm;):</strong> consentimento livre,
          informado e específico (inciso I) e legítimo interesse na execução
          de etapas pré-contratuais a pedido do titular (inciso V), conforme
          aplicável a cada etapa.
        </p>

        <h2>4. Compartilhamento de dados</h2>
        <p>
          <strong>Não compartilhamos, vendemos ou cedemos</strong> seus dados
          de contato a terceiros para finalidade de marketing.
        </p>
        <p>
          Os dados pessoais coletados via landing são tratados internamente
          pela equipe comercial do Lintty.
        </p>
        <p>
          <strong>
            Sub-operadores que processam dados quando o produto é ativado
          </strong>{" "}
          (não na landing &mdash; apenas quando você se torna cliente e nos
          fornece código-fonte para análise):
        </p>
        <table>
          <thead>
            <tr>
              <th>Sub-operador</th>
              <th>Função</th>
              <th>Localização</th>
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
              <td>DPA padrão GCP, criptografia em trânsito e repouso</td>
            </tr>
          </tbody>
        </table>
        <p>
          <strong>Nota sobre a versão atual do produto (Sales Cut):</strong> a
          análise de código roda{" "}
          <strong>localmente no laptop do operador</strong>, sem envio a
          provedor de IA. Não há sub-operador de inferência de modelo de
          linguagem na cadeia. Em versão futura (V1+), pretendemos adicionar
          Anthropic, PBC como sub-operador para análise semântica complementar,
          sob{" "}
          <strong>
            Zero Data Retention (ZDR) contratualizado
          </strong>
          . Esta página será atualizada antes da ativação desse sub-operador.
        </p>
        <p>
          A relação completa de sub-operadores e respectivos termos é fornecida
          ao cliente no momento da contratação, no DPA. Como cliente
          enterprise, você pode solicitar atualização da lista a qualquer
          tempo.
        </p>

        <h2>5. Seus direitos como titular (LGPD art. 18)</h2>
        <p>Você tem o direito de, a qualquer tempo, mediante requisição:</p>
        <ul>
          <li>
            <strong>Confirmar</strong> a existência de tratamento dos seus
            dados;
          </li>
          <li>
            <strong>Acessar</strong> os dados que mantemos sobre você;
          </li>
          <li>
            <strong>Corrigir</strong> dados incompletos, inexatos ou
            desatualizados;
          </li>
          <li>
            <strong>Anonimizar, bloquear ou eliminar</strong> dados
            desnecessários, excessivos ou tratados em desconformidade com a
            LGPD;
          </li>
          <li>
            <strong>Solicitar a portabilidade</strong> dos seus dados a outro
            fornecedor de serviço, observados os segredos comercial e
            industrial;
          </li>
          <li>
            <strong>Eliminar</strong> os dados pessoais tratados com base no
            seu consentimento;
          </li>
          <li>
            <strong>Obter informação</strong> sobre as entidades públicas e
            privadas com as quais compartilhamos seus dados;
          </li>
          <li>
            <strong>Obter informação</strong> sobre a possibilidade de não
            fornecer consentimento e suas consequências;
          </li>
          <li>
            <strong>Revogar o consentimento</strong>, quando aplicável.
          </li>
        </ul>
        <p>
          <strong>Canal para exercício dos direitos:</strong>{" "}
          <a href="mailto:privacidade@lintty.com">privacidade@lintty.com</a>.
        </p>
        <p>
          <strong>Prazo de resposta:</strong> até{" "}
          <strong>15 (quinze) dias</strong> corridos contados da sua
          requisição.
        </p>
        <p>
          Para sua segurança, podemos solicitar informações adicionais para
          confirmação de identidade antes de atender à requisição.
        </p>

        <h2>6. Retenção de dados</h2>
        <table>
          <thead>
            <tr>
              <th>Categoria</th>
              <th>Prazo de retenção</th>
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
              <td>Exclusão automática</td>
            </tr>
            <tr>
              <td>Leads que viraram clientes</td>
              <td>Conforme DPA da relação contratual</td>
              <td>Conforme DPA</td>
            </tr>
            <tr>
              <td>Logs de aplicação (acessos ao site)</td>
              <td>90 dias</td>
              <td>Pseudo-anonimização e expurgo</td>
            </tr>
            <tr>
              <td>Comunicações por email</td>
              <td>
                Conforme política de retenção interna do provedor de email + 24
                meses para fins de prestação de contas comerciais
              </td>
              <td>Exclusão</td>
            </tr>
          </tbody>
        </table>
        <p>
          Você pode solicitar a exclusão imediata dos seus dados a qualquer
          tempo via canal indicado em &sect;5.
        </p>

        <h2>7. Cookies e rastreamento</h2>
        <p>
          A landing page do Lintty{" "}
          <strong>
            não utiliza cookies de rastreamento de terceiros
          </strong>{" "}
          (Google Analytics, Facebook Pixel, ferramentas de remarketing).
        </p>
        <p>
          [Caso utilize Plausible Analytics ou Umami: declarar aqui &mdash;
          &ldquo;Utilizamos a ferramenta de analytics Plausible/Umami, que
          opera sem cookies e sem coleta de dados pessoais identificáveis, em
          conformidade com LGPD/GDPR.&rdquo;]
        </p>
        <p>
          Cookies estritamente técnicos podem ser utilizados para funcionamento
          da página (ex: preferência de idioma) e não armazenam dados
          pessoais.
        </p>

        <h2>8. Segurança</h2>
        <p>
          Adotamos medidas técnicas e organizacionais para proteger seus dados
          contra acessos não autorizados, perda, alteração ou destruição,
          incluindo:
        </p>
        <ul>
          <li>Comunicação criptografada (TLS 1.3);</li>
          <li>Princípio do menor privilégio nos acessos internos;</li>
          <li>Autenticação multifator obrigatória para a equipe;</li>
          <li>Pseudo-anonimização em logs de aplicação;</li>
          <li>Auditoria periódica de acessos.</li>
        </ul>
        <p>
          Apesar de todos os esforços, nenhum sistema é absolutamente
          invulnerável. Em caso de incidente que envolva seus dados,
          comunicaremos você e a Autoridade Nacional de Proteção de Dados
          (ANPD) nos prazos previstos na LGPD.
        </p>

        <h2>9. Encarregado pelo Tratamento de Dados (DPO)</h2>
        <p>Para os fins do art. 41 da LGPD, designamos como Encarregado:</p>
        <p>
          <strong>[INSERIR NOME DO ENCARREGADO]</strong> (no Modo Projeto, pode
          ser o próprio Operador ou DPO terceirizado).
          <br />
          <strong>Email:</strong>{" "}
          <a href="mailto:privacidade@lintty.com">privacidade@lintty.com</a>
        </p>
        <p className="text-sm text-neutral-400">
          <strong>Modo Projeto:</strong> com a constituição da PJ, esta
          designação será revisada e oficializada conforme art. 41 da LGPD.
        </p>

        <h2>10. Alterações a esta Política</h2>
        <p>
          Esta Política pode ser atualizada periodicamente. Sempre que houver
          alteração relevante, indicaremos a nova{" "}
          <strong>data da última revisão</strong> no topo do documento e,
          quando aplicável, comunicaremos os titulares cujos dados estejam em
          tratamento ativo.
        </p>
        <p>
          <strong>Versão atual:</strong> 1.0
          <br />
          <strong>Data da última revisão:</strong> 27 de abril de 2026.
        </p>

        <h2>11. Contato</h2>
        <p>Dúvidas sobre esta Política ou sobre o tratamento dos seus dados:</p>
        <ul>
          <li>
            <strong>Privacidade e LGPD:</strong>{" "}
            <a href="mailto:privacidade@lintty.com">privacidade@lintty.com</a>
          </li>
          <li>
            <strong>Segurança:</strong>{" "}
            <a href="mailto:seguranca@lintty.com">seguranca@lintty.com</a>
          </li>
          <li>
            <strong>Comercial:</strong> [INSERIR EMAIL COMERCIAL]
          </li>
        </ul>
        </article>
      </main>
    </>
  );
}
