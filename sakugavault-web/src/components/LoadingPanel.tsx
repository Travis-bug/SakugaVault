export function LoadingPanel({ title, message }: { title: string; message: string }) {
  return (
    <div className="loading-screen">
      <div className="loading-panel reveal">
        <span className="eyebrow">SakugaVault</span>
        <h1>{title}</h1>
        <p>{message}</p>
      </div>
    </div>
  );
}
