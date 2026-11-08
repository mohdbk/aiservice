import { createContext, ReactNode, useCallback, useContext, useEffect, useState } from "react";
import { api, User } from "./api";

type Auth = {
  user: User | null;
  ready: boolean;
  signIn: (email: string, password: string) => Promise<void>;
  signUp: (full_name: string, email: string, password: string, lang: string) => Promise<void>;
  signOut: () => Promise<void>;
  clear: () => void;
};

const AuthContext = createContext<Auth | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [ready, setReady] = useState(false);
  useEffect(() => {
    api<{ user: User | null }>("/api/me")
      .then((r) => setUser(r.user))
      .catch(() => setUser(null))
      .finally(() => setReady(true));
  }, []);
  const signIn = useCallback(async (email: string, password: string) => {
    const r = await api<{ user: User }>("/api/login", { body: { email, password } });
    setUser(r.user);
  }, []);
  const signUp = useCallback(async (full_name: string, email: string, password: string, lang: string) => {
    const r = await api<{ user: User }>("/api/signup", { body: { full_name, email, password, lang } });
    setUser(r.user);
  }, []);
  const signOut = useCallback(async () => {
    try {
      await api("/api/logout", { body: {} });
    } finally {
      setUser(null);
    }
  }, []);
  const clear = useCallback(() => setUser(null), []);
  return <AuthContext.Provider value={{ user, ready, signIn, signUp, signOut, clear }}>{children}</AuthContext.Provider>;
}

export function useAuth(): Auth {
  const a = useContext(AuthContext);
  if (!a) throw new Error("useAuth outside AuthProvider");
  return a;
}
