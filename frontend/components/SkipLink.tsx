/**
 * Visible-only-on-focus skip link. Same a11y pattern as the legacy
 * pages — first focusable element on the page, jumps the keyboard
 * user past the header straight to <main id="main">.
 */
export function SkipLink() {
  return (
    <a href="#main" className="skip">
      Pular para o conteudo
    </a>
  );
}
