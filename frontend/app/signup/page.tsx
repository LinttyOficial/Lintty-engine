"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useState, type FormEvent } from "react";
import { Header } from "@/components/Header";
import { Footer } from "@/components/Footer";
import { SkipLink } from "@/components/SkipLink";
import { backendUrl } from "@/lib/api";
import { useAuth } from "@/lib/auth";

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

interface FieldErrors {
  displayName?: string;
  email?: string;
  password?: string;
  orgName?: string;
  acceptTerms?: string;
}

export default function SignupPage() {
  const router = useRouter();
  const { isAuthenticated, isLoading, signup } = useAuth();

  // already-logged-in users get bounced to /dashboard
  useEffect(() => {
    if (!isLoading && isAuthenticated) {
      router.replace("/dashboard");
    }
  }, [isAuthenticated, isLoading, router]);

  const [displayName, setDisplayName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [orgName, setOrgName] = useState("");
  const [acceptTerms, setAcceptTerms] = useState(false);
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
  const [globalError, setGlobalError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  // Password strength heuristic — same logic as the legacy page.
  const pwdStrength = (() => {
    if (!password) return { class: "", label: "Mínimo 8 caracteres." };
    let score = 0;
    if (password.length >= 8) score++;
    if (password.length >= 12) score++;
    if (/[A-Z]/.test(password) && /[a-z]/.test(password)) score++;
    if (/\d/.test(password)) score++;
    if (/[^A-Za-z0-9]/.test(password)) score++;

    if (password.length < 8) {
      return { class: "weak", label: "Muito curta (mínimo 8 caracteres)." };
    }
    if (score <= 2) {
      return {
        class: "weak",
        label: "Senha fraca — tente misturar letras, números e símbolos.",
      };
    }
    if (score <= 4) {
      return { class: "medium", label: "Senha média." };
    }
    return { class: "strong", label: "Senha forte." };
  })();

  function validate(): boolean {
    const errs: FieldErrors = {};
    if (!displayName.trim()) errs.displayName = "Informe seu nome.";
    if (!email.trim()) errs.email = "Informe um email.";
    else if (!EMAIL_RE.test(email.trim())) errs.email = "Email inválido.";
    if (!password) errs.password = "Informe uma senha.";
    else if (password.length < 8)
      errs.password = "Senha precisa ter pelo menos 8 caracteres.";
    if (!orgName.trim()) errs.orgName = "Informe o nome da organização.";
    if (!acceptTerms)
      errs.acceptTerms = "Você precisa aceitar os termos para continuar.";
    setFieldErrors(errs);
    return Object.keys(errs).length === 0;
  }

  async function handleSubmit(ev: FormEvent<HTMLFormElement>) {
    ev.preventDefault();
    setGlobalError(null);
    if (!validate()) {
      // focus first errored field
      const order: Array<keyof FieldErrors> = [
        "displayName",
        "email",
        "password",
        "orgName",
        "acceptTerms",
      ];
      const first = order.find((k) => fieldErrors[k]);
      if (first) {
        const el = document.getElementById(first);
        if (el) el.focus();
      }
      return;
    }

    setSubmitting(true);
    try {
      const res = await signup({
        displayName: displayName.trim(),
        email: email.trim(),
        password,
        orgName: orgName.trim(),
      });
      if (res.ok) {
        router.replace("/dashboard");
        return;
      }
      // 409 — conflict (email or org slug)
      if (res.status === 409) {
        const body = res.body as { error?: string; errors?: Record<string, string[]> } | null;
        if (
          body?.error === "org_slug_taken" ||
          (body?.errors && body.errors.orgName)
        ) {
          setFieldErrors((prev) => ({
            ...prev,
            orgName: "Já existe uma organização com esse nome. Escolha outro.",
          }));
          document.getElementById("orgName")?.focus();
          return;
        }
        setFieldErrors((prev) => ({
          ...prev,
          email: "Email já cadastrado. Tente entrar.",
        }));
        document.getElementById("email")?.focus();
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
      const body = res.body as { message?: string } | null;
      setGlobalError(
        body?.message ?? `Erro ${res.status}. Tente novamente.`,
      );
    } catch (err) {
      console.error("POST /api/auth/signup falhou", err);
      setGlobalError(
        "Não foi possível contatar o servidor. Em desenvolvimento, suba o backend com: dotnet run --project engine/src/Lintty.WebInspector. Em produção, verifique sua conexão e tente novamente.",
      );
    } finally {
      setSubmitting(false);
    }
  }

  function setFieldError(field: keyof FieldErrors, msg: string | undefined) {
    setFieldErrors((prev) => ({ ...prev, [field]: msg }));
  }

  return (
    <>
      <SkipLink />
      <Header />
      <main id="main" className="px-6 py-12 md:py-16">
        <div className="max-w-md mx-auto">
          <p className="text-xs font-semibold tracking-widest text-neutral-500 uppercase mb-3">
            Lintty Dashboard &middot; Beta
          </p>
          <h1 className="text-3xl md:text-4xl font-bold leading-tight tracking-tight">
            Crie sua conta Lintty.
          </h1>
          <p className="mt-3 text-neutral-700">
            Tenha laudos byte-determinísticos da sua equipe inteira.
          </p>

          <form
            id="signup-form"
            noValidate
            onSubmit={handleSubmit}
            className="mt-8 bg-white border border-neutral-200 rounded-xl p-6 md:p-8 shadow-sm space-y-5"
          >
            <div>
              <label htmlFor="displayName" className="block text-sm font-semibold mb-2">
                Nome completo <span className="text-sinner" aria-hidden="true">*</span>
              </label>
              <input
                id="displayName"
                name="displayName"
                type="text"
                required
                autoComplete="name"
                placeholder="Maria Silva"
                value={displayName}
                onChange={(e) => {
                  setDisplayName(e.target.value);
                  setFieldError("displayName", undefined);
                }}
                aria-invalid={fieldErrors.displayName ? "true" : undefined}
                className="w-full px-4 py-3 border border-neutral-300 rounded-md text-sm bg-white focus:border-saint focus:outline-none transition"
              />
              {fieldErrors.displayName && (
                <p className="field-error" role="alert" aria-live="polite">
                  {fieldErrors.displayName}
                </p>
              )}
            </div>

            <div>
              <label htmlFor="email" className="block text-sm font-semibold mb-2">
                Email corporativo <span className="text-sinner" aria-hidden="true">*</span>
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
                  setFieldError("email", undefined);
                }}
                aria-invalid={fieldErrors.email ? "true" : undefined}
                className="w-full px-4 py-3 border border-neutral-300 rounded-md text-sm bg-white focus:border-saint focus:outline-none transition"
              />
              {fieldErrors.email && (
                <p className="field-error" role="alert" aria-live="polite">
                  {fieldErrors.email}
                </p>
              )}
            </div>

            <div>
              <label htmlFor="password" className="block text-sm font-semibold mb-2">
                Senha <span className="text-sinner" aria-hidden="true">*</span>
              </label>
              <input
                id="password"
                name="password"
                type="password"
                required
                minLength={8}
                autoComplete="new-password"
                placeholder="Mínimo 8 caracteres"
                value={password}
                onChange={(e) => {
                  setPassword(e.target.value);
                  setFieldError("password", undefined);
                }}
                aria-invalid={fieldErrors.password ? "true" : undefined}
                className="w-full px-4 py-3 border border-neutral-300 rounded-md text-sm bg-white focus:border-saint focus:outline-none transition"
              />
              <div className="pwd-strength" aria-hidden="true">
                <div className={`pwd-strength-bar ${pwdStrength.class}`} />
              </div>
              <p className="mt-2 text-xs text-neutral-500">{pwdStrength.label}</p>
              {fieldErrors.password && (
                <p className="field-error" role="alert" aria-live="polite">
                  {fieldErrors.password}
                </p>
              )}
            </div>

            <div>
              <label htmlFor="orgName" className="block text-sm font-semibold mb-2">
                Nome da organização <span className="text-sinner" aria-hidden="true">*</span>
              </label>
              <input
                id="orgName"
                name="orgName"
                type="text"
                required
                autoComplete="organization"
                placeholder="Acme Varejo S.A."
                value={orgName}
                onChange={(e) => {
                  setOrgName(e.target.value);
                  setFieldError("orgName", undefined);
                }}
                aria-invalid={fieldErrors.orgName ? "true" : undefined}
                className="w-full px-4 py-3 border border-neutral-300 rounded-md text-sm bg-white focus:border-saint focus:outline-none transition"
              />
              <p className="mt-2 text-xs text-neutral-500">
                É o workspace da sua equipe. Você pode convidar usuários depois.
              </p>
              {fieldErrors.orgName && (
                <p className="field-error" role="alert" aria-live="polite">
                  {fieldErrors.orgName}
                </p>
              )}
            </div>

            <div className="pt-1">
              <label className="flex items-start gap-3 text-sm text-neutral-700 cursor-pointer">
                <input
                  id="acceptTerms"
                  name="acceptTerms"
                  type="checkbox"
                  required
                  checked={acceptTerms}
                  onChange={(e) => {
                    setAcceptTerms(e.target.checked);
                    setFieldError("acceptTerms", undefined);
                  }}
                  className="mt-1 w-4 h-4 border-neutral-400 rounded text-saint focus:ring-saint"
                />
                <span>
                  Aceito os termos de uso e a{" "}
                  <Link
                    href="/privacidade"
                    className="underline decoration-neutral-400 underline-offset-2 hover:text-ink hover:decoration-ink"
                  >
                    política de privacidade
                  </Link>
                  .
                </span>
              </label>
              {fieldErrors.acceptTerms && (
                <p className="field-error" role="alert" aria-live="polite">
                  {fieldErrors.acceptTerms}
                </p>
              )}
            </div>

            {globalError && (
              <div
                className="bg-sinner-bg border border-sinner/20 text-sinner text-sm rounded-md px-4 py-3"
                role="alert"
                aria-live="polite"
              >
                {globalError}
              </div>
            )}

            <button
              type="submit"
              disabled={submitting}
              className="w-full inline-flex items-center justify-center gap-2 px-6 py-3 rounded-md bg-ink text-white font-semibold hover:bg-neutral-800 transition disabled:opacity-50 disabled:cursor-not-allowed"
            >
              <span>{submitting ? "Criando conta..." : "Criar conta"}</span>
              <span aria-hidden="true">→</span>
            </button>
          </form>

          <div className="my-6 flex items-center gap-4" aria-hidden="true">
            <div className="flex-1 h-px bg-neutral-200" />
            <span className="text-xs uppercase tracking-widest text-neutral-500">ou</span>
            <div className="flex-1 h-px bg-neutral-200" />
          </div>

          <button
            type="button"
            onClick={() => {
              window.location.href = backendUrl("/api/auth/github/start");
            }}
            className="w-full inline-flex items-center justify-center gap-3 px-6 py-3 rounded-md border border-neutral-300 bg-white text-neutral-800 font-medium hover:bg-neutral-100 transition"
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

          <p className="mt-6 text-center text-sm text-neutral-700">
            Já tem conta?{" "}
            <Link
              href="/login"
              className="font-semibold underline decoration-neutral-400 underline-offset-2 hover:text-ink hover:decoration-ink"
            >
              Entrar
            </Link>
          </p>
        </div>
      </main>
      <Footer />
    </>
  );
}
