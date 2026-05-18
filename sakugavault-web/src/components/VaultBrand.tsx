import { useEffect, useRef, useState, type CSSProperties } from "react";

interface VaultBrandProps {
  mode?: "default" | "compact" | "hero";
  subtitle?: string;
}

export function VaultBrand({ mode = "default", subtitle = "Warm signal streaming node" }: VaultBrandProps) {
  const brandRef = useRef<HTMLDivElement | null>(null);
  const [pulseIndex, setPulseIndex] = useState(0);

  useEffect(() => {
    const intervalId = window.setInterval(() => {
      setPulseIndex((current) => (current + 1) % 6);
    }, 1350);

    return () => window.clearInterval(intervalId);
  }, []);

  function handlePointerMove(event: React.PointerEvent<HTMLDivElement>) {
    const element = brandRef.current;
    if (!element) {
      return;
    }

    const rect = element.getBoundingClientRect();
    const x = ((event.clientX - rect.left) / rect.width - 0.5) * 2;
    const y = ((event.clientY - rect.top) / rect.height - 0.5) * 2;

    element.style.setProperty("--brand-tilt-x", `${y * -7}deg`);
    element.style.setProperty("--brand-tilt-y", `${x * 10}deg`);
    element.style.setProperty("--brand-glow-x", `${50 + x * 18}%`);
    element.style.setProperty("--brand-glow-y", `${50 + y * 18}%`);
  }

  function resetPointerState() {
    const element = brandRef.current;
    if (!element) {
      return;
    }

    element.style.setProperty("--brand-tilt-x", "0deg");
    element.style.setProperty("--brand-tilt-y", "0deg");
    element.style.setProperty("--brand-glow-x", "50%");
    element.style.setProperty("--brand-glow-y", "50%");
  }

  return (
    <div
      ref={brandRef}
      className={`vault-brand vault-brand--${mode}`}
      onPointerMove={handlePointerMove}
      onPointerLeave={resetPointerState}
    >
      <div className="vault-brand__mark" aria-hidden="true">
        <div className="vault-brand__glow" />
        <div className="vault-brand__ring vault-brand__ring--outer" />
        <div className="vault-brand__ring vault-brand__ring--middle" />
        <div className="vault-brand__ring vault-brand__ring--inner" />
        <div className="vault-brand__core">
          <span className="vault-brand__core-dot" />
        </div>
        {Array.from({ length: 6 }, (_, index) => (
          <span
            key={index}
            className={`vault-brand__node ${pulseIndex === index ? "is-live" : ""}`}
            style={{ "--node-index": index } as CSSProperties}
          />
        ))}
        <div className="vault-brand__bars">
          {Array.from({ length: 3 }, (_, index) => (
            <span
              key={index}
              className={`vault-brand__bar ${pulseIndex % 3 === index ? "is-live" : ""}`}
            />
          ))}
        </div>
      </div>
      <div className="vault-brand__text">
        <div className="vault-brand__wordmark">
          <span>Sakuga</span>
          <span>Vault</span>
        </div>
        <p>{subtitle}</p>
      </div>
    </div>
  );
}
