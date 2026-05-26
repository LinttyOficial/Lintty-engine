"use client";

import {
  useEffect,
  useRef,
  useState,
  type CSSProperties,
  type ReactNode,
} from "react";

interface FadeInProps {
  children: ReactNode;
  /** Atraso em ms para escalonar entradas próximas. */
  delayMs?: number;
  /** Margem inferior do viewport antes de revelar (default -10%). */
  rootMargin?: string;
  /** Tag HTML do wrapper (default "div"). */
  as?: "div" | "section" | "article" | "header" | "ul" | "li";
  className?: string;
}

/**
 * Wrapper client-only que aplica `.lt-fade-up-init` no mount e troca para
 * `is-visible` quando o elemento entra na viewport via IntersectionObserver.
 * Uma única observação por elemento — depois desconecta.
 */
export function FadeIn({
  children,
  delayMs = 0,
  rootMargin = "0px 0px -10% 0px",
  as: Tag = "div",
  className = "",
}: FadeInProps) {
  const ref = useRef<HTMLElement | null>(null);
  const [visible, setVisible] = useState(false);

  useEffect(() => {
    const el = ref.current;
    if (!el) return undefined;
    if (typeof IntersectionObserver === "undefined") {
      setVisible(true);
      return undefined;
    }
    const io = new IntersectionObserver(
      (entries) => {
        for (const entry of entries) {
          if (entry.isIntersecting) {
            setVisible(true);
            io.disconnect();
            break;
          }
        }
      },
      { rootMargin, threshold: 0.05 },
    );
    io.observe(el);
    return () => io.disconnect();
  }, [rootMargin]);

  const style: CSSProperties | undefined =
    delayMs > 0 ? { transitionDelay: `${delayMs}ms` } : undefined;

  return (
    <Tag
      // @ts-expect-error — ref typed for several element kinds
      ref={ref}
      style={style}
      className={`lt-fade-up-init${visible ? " is-visible" : ""} ${className}`.trim()}
    >
      {children}
    </Tag>
  );
}
