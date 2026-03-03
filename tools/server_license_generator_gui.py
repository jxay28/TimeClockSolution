import hashlib
import hmac
import tkinter as tk
from tkinter import messagebox

SECRET = b"TimeClock.Server.License.v1::2026"


def generate_key_from_token(token: str) -> str:
    normalized = token.strip().encode("utf-8")
    digest = hmac.new(SECRET, normalized, hashlib.sha256).hexdigest().upper()
    return f"{digest[0:8]}-{digest[8:16]}-{digest[16:24]}-{digest[24:32]}"


class LicenseGeneratorApp:
    def __init__(self, root: tk.Tk) -> None:
        self.root = root
        self.root.title("TimeClock Server - License Key Generator")
        self.root.geometry("620x230")
        self.root.resizable(False, False)

        token_label = tk.Label(root, text="Token:")
        token_label.pack(anchor="w", padx=14, pady=(14, 4))

        self.token_entry = tk.Entry(root, font=("Consolas", 12))
        self.token_entry.pack(fill="x", padx=14)

        key_label = tk.Label(root, text="Key generata:")
        key_label.pack(anchor="w", padx=14, pady=(14, 4))

        self.key_var = tk.StringVar()
        self.key_entry = tk.Entry(root, textvariable=self.key_var, font=("Consolas", 12), state="readonly")
        self.key_entry.pack(fill="x", padx=14)

        buttons = tk.Frame(root)
        buttons.pack(fill="x", padx=14, pady=16)

        generate_btn = tk.Button(buttons, text="Genera", width=14, command=self.on_generate)
        generate_btn.pack(side="left")

        copy_btn = tk.Button(buttons, text="Copia key", width=14, command=self.on_copy)
        copy_btn.pack(side="left", padx=(10, 0))

    def on_generate(self) -> None:
        token = self.token_entry.get().strip()
        if not token:
            messagebox.showwarning("Token mancante", "Inserisci un token.")
            return

        self.key_var.set(generate_key_from_token(token))

    def on_copy(self) -> None:
        key = self.key_var.get().strip()
        if not key:
            messagebox.showwarning("Key mancante", "Genera prima una key.")
            return

        self.root.clipboard_clear()
        self.root.clipboard_append(key)
        self.root.update()
        messagebox.showinfo("Copiato", "Key copiata negli appunti.")


def main() -> None:
    root = tk.Tk()
    LicenseGeneratorApp(root)
    root.mainloop()


if __name__ == "__main__":
    main()
