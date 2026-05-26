"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useRef, useState, type FormEvent } from "react";
import { Header } from "@/components/Header";
import { Footer } from "@/components/Footer";
import { SkipLink } from "@/components/SkipLink";
import { AuroraPageHeader } from "@/components/HeroAurora";
import { backendUrl } from "@/lib/api";
import { useAuth } from "@/lib/auth";

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

interface FieldErrors {
  email?: string;
  password?: string;
}

export default function LoginPage() {
  const router = useRouter();
  const { isAuthenticated, isLoading, login } = useAuth();

  useEffect(() => {
    if (!isLoading && isAuthenticated) {
      router.replace("/dashboard");
    }
  }, [isAuthenticated, isLoading, router]);

  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
  const [globalError, setGlobalError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [showForgot, setShowForgot] = useState(false);

  const forgotLinkRef = useRef<HTMLButtonElement>(null);
  const forgotCloseRef = useRef<HTMLButtonElement>(null);

  // Modal: focus + Escape close, mirroring the legacy login.html.
  useEffect(() => {
    if (!showForgot) return undefined;
    setTimeout(() => forgotCloseRef.current?.focus(), 50);

    const handleKey = (ev: KeyboardEvent) => {
      if (ev.key === "Escape") {
        setShowForgot(false);
        forgotLinkRef.current?.focus();
      }
    };
    document.addEventListener("keydown", handleKey);
    return () => {
      document.removeEventListener("keydown", handleKey);
    };
  }, [showForgot]);

  function validate(): boolean {
    const errs: FieldErrors = {};
    if (!email.trim()) errs.email = "Informe seu email.";
    else if (!EMAIL_RE.test(email.trim())) errs.email = "Email inválido.";
    if (!password) errs.password = "Informe sua senha.";
    setFieldErrors(errs);
    return Object.keys(errs).length === 0;
  }

  async function handleSubmit(ev: FormEvent<HTMLFormElement>) {
    ev.preventDefault();
    setGlobalError(null);
    if (!validate()) return;

    setSubmitting(true);
    try {
      const res = await login({ email: email.trim(), password });
      if (res.ok) {
        router.replace("/dashboard");
        return;
      }
      if (res.status === 401) {
        // Mensagem genérica — não vaza qual está errado (mitiga enumeration)
        setGlobalError("Email ou senha incorretos.");
        return;
      }
      if (res.status === 400) {
        const body = res.body as
          | { errors?: Record<string, string[] | string> }
          | null;
        if (body?.errors) {
          const next: FieldErrors = {};
          Object.entries(body.errors).forEach(([k, v]) => {
            const msg = Array.isArray(v) ? v[0] : String(v);
            (next as Record<string, string>)[k] = msg;
          });
          setFieldErrors(next);
          return;
        }
      }
      if (res.status === 429) {
        setGlobalError(
          "Muitas tentativas. Aguarde alguns minutos antes de tentar novamente.",
        );
        return;
      }
      const body = res.body as { message?: string } | null;
      setGlobalError(body?.message ?? `Erro ${res.status}. Tente novamente.`);
    } catch (err) {
      console.error("POST /api/auth/login falhou", err);
      setGlobalError(
        "Não foi possível contatar o servidor. Em desenvolvimento, suba o backend com: dotnet run --project engine/src/Lintty.WebInspector. Em produção, verifique sua conexão e tente novamente.",
      );
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <>
      <SkipLink />
      <Header />
      <AuroraPageHeader
        maxWidthClass="max-w-md"
        eyebrow="Lintty Dashboard"
        title="Entrar na sua conta."
        subtitle="Acesse seu workspace e os laudos da sua equipe."
      />
      <main id="main" className="lt-dark-glow lt-noise px-6 py-12 md:py-16 text-paper">
        <div className="max-w-md mx-auto">
          <form
            noValidate
            onSubmit={handleSubmit}
            className="mt-8 lt-card-form p-6 md:p-8 space-y-5"
          >
            <div>
              <label htmlFor="email" className="block text-sm font-semibold mb-2 text-paper">
                Email <span className="text-red-300" aria-hidden="true">*</span>
              </label>
              <input
                id="email"
                name="email"
                type="email"
                required
                autoComplete="email"
                placeholder="maria@empresa.com.br"
                value={email}
                onChange={(e) => {
                  setEmail(e.target.value);
                  setFieldErrors((prev) => ({ ...prev, email: undefined }));
                }}
                aria-invalid={fieldErrors.email ? "true" : undefined}
                className="lt-input"
              />
              {fieldErrors.email && (
                <p className="field-error" role="alert" aria-live="polite">
                  {fieldErrors.email}
                </p>
              )}
            </div>

            <div>
              <div className="flex items-center justify-between mb-2">
                <label htmlFor="password" className="block text-sm font-semibold text-paper">
                  Senha <span className="text-red-300" aria-hidden="true">*</span>
                </label>
                <button
                  ref={forgotLinkRef}
                  type="button"
                  onClick={() => setShowForgot(true)}
                  className="text-xs text-neutral-400 hover:text-paper underline decoration-neutral-600 underline-offset-2"
                >
                  Esqueceu a senha?
                </button>
              </div>
              <input
                id="password"
                name="password"
                type="password"
                required
                autoComplete="current-password"
                value={password}
                onChange={(e) => {
                  setPassword(e.target.value);
                  setFieldErrors((prev) => ({ ...prev, password: undefined }));
                }}
                aria-invalid={fieldErrors.password ? "true" : undefined}
                className="lt-input"
              />
              {fieldErrors.password && (
                <p className="field-error" role="alert" aria-live="polite">
                  {fieldErrors.password}
                </p>
              )}
            </div>

            {globalError && (
              <div
                className="lt-alert-danger"
                role="alert"
                aria-live="polite"
              >
                {globalError}
              </div>
            )}

            <button
              type="submit"
              disabled={submitting}
              className="w-full lt-btn-primary"
            >
              <span>{submitting ? "Entrando..." : "Entrar"}</span>
              <span aria-hidden="true">→</span>
            </button>
          </form>

          <div className="my-6 flex items-center gap-4" aria-hidden="true">
            <div className="flex-1 h-px bg-neutral-800/60" />
            <span className="text-xs uppercase tracking-widest text-neutral-500">ou</span>
            <div className="flex-1 h-px bg-neutral-800/60" />
          </div>

          <button
            type="button"
            onClick={() => {
              window.location.href = backendUrl("/api/auth/github/start");
            }}
            className="w-full lt-btn-secondary gap-3"
          >
            <svg
              width="20"
              height="20"
              viewBox="0 0 24 24"
              fill="currentColor"
              aria-hidden="true"
            >
              <path d="M12 .5C5.65.5.5 5.65.5 12c0 5.08 3.29 9.39 7.86 10.91.57.1.78-.25.78-.55 0-.27-.01-1-.02-1.96-3.2.69-3.87-1.54-3.87-1.54-.52-1.32-1.27-1.67-1.27-1.67-1.04-.71.08-.7.08-.7 1.15.08 1.76 1.18 1.76 1.18 1.02 1.75 2.68 1.25 3.34.96.1-.74.4-1.25.72-1.54-2.55-.29-5.24-1.28-5.24-5.7 0-1.26.45-2.29 1.18-3.1-.12-.29-.51-1.46.11-3.05 0 0 .96-.31 3.16 1.18a10.97 10.97 0 0 1 5.76 0c2.2-1.49 3.16-1.18 3.16-1.18.62 1.59.23 2.76.11 3.05.74.81 1.18 1.84 1.18 3.1 0 4.43-2.69 5.41-5.26 5.69.41.36.78 1.06.78 2.13 0 1.54-.01 2.78-.01 3.16 0 .31.21.66.79.55C20.21 21.39 23.5 17.07 23.5 12 23.5 5.65 18.35.5 12 .5z" />
            </svg>
            Continuar com GitHub
          </button>

          <p className="mt-6 text-center text-sm text-neutral-400">
            Não tem conta?{" "}
            <Link
              href="/signup"
              className="font-semibold underline decoration-emerald-400/50 underline-offset-2 text-emerald-200 hover:text-emerald-100"
            >
              Criar conta
            </Link>
          </p>
        </div>
      </main>

      {/* Modal recovery senha (V1.1) */}
      <div
        className={`lt-modal-backdrop${showForgot ? " is-open" : ""}`}
        role="dialog"
        aria-modal="true"
        aria-labelledby="forgot-title"
        aria-describedby="forgot-desc"
        onClick={(ev) => {
          if (ev.target === ev.currentTarget) setShowForgot(false);
        }}
      >
        <div className="lt-modal">
          <h2 id="forgot-title" className="text-xl font-bold tracking-tight text-paper">
            Recuperação de senha em V1.1
          </h2>
          <p id="forgot-desc" className="mt-3 text-sm text-neutral-300">
            Estamos no Beta do dashboard. O fluxo automatizado de redefinição de senha entra
            em V1.1.
          </p>
          <p className="mt-3 text-sm text-neutral-300">
            Por enquanto, mande um email para{" "}
            <a
              href="mailto:vinicius@landtech.com.br?subject=Reset%20de%20senha%20Lintty"
              className="font-semibold underline decoration-emerald-400/50 underline-offset-2 text-emerald-200 hover:text-emerald-100"
            >
              vinicius@landtech.com.br
            </a>{" "}
            que resetamos manualmente em até 1 dia útil.
          </p>
          <p className="mt-3 text-xs text-neutral-500">
            Preferimos honestidade a falsa promessa de feature que não existe ainda.
          </p>
          <div className="mt-6 flex justify-end gap-3">
            <button
              ref={forgotCloseRef}
              type="button"
              onClick={() => {
                setShowForgot(false);
                forgotLinkRef.current?.focus();
              }}
              className="lt-btn-primary text-sm"
            >
              Entendi
            </button>
          </div>
        </div>
      </div>

      <Footer />
    </>
  );
}
