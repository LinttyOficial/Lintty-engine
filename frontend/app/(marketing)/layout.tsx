import { Header } from "@/components/Header";
import { Footer } from "@/components/Footer";
import { SkipLink } from "@/components/SkipLink";

/**
 * Marketing shell — Header + Footer + skip link wrapping every public page
 * (`/`, `/cli`, `/privacidade`, `/pricing`). Mirrors the trio that the
 * legacy HTML pages copy-pasted at the top and bottom of each file.
 */
export default function MarketingLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <>
      <SkipLink />
      <Header />
      {children}
      <Footer />
    </>
  );
}
