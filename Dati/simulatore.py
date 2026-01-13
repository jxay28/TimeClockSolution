import csv
import os
from datetime import datetime, timedelta
import random

def genera_timbrature_storico(user_id, anni_durata=10):
    # Il file deve chiamarsi ID.csv per essere letto dal server 
    file_path = f"{user_id}.csv"
    data_fine = datetime.now()
    # Calcolo data inizio basato su 10 anni
    data_inizio = data_fine - timedelta(days=anni_durata * 365)
    
    timbrature = []
    data_corrente = data_inizio

    print(f"Generazione in corso per l'utente {user_id} dal {data_inizio.year} al {data_fine.year}...")

    while data_corrente <= data_fine:
        # Generiamo timbrature Lun-Ven (weekday < 5)
        if data_corrente.weekday() < 5:
            
            # --- TURNO MATTINA ---
            # Punto di riferimento: ore 08:00. Variamo di +/- 15 minuti.
            base_m = data_corrente.replace(hour=8, minute=0, second=0, microsecond=0)
            e1 = base_m + timedelta(minutes=random.randint(-10, 20))
            timbrature.append([e1.strftime("%Y-%m-%d %H:%M"), "Entrata"])
            
            # Punto di riferimento: ore 13:00.
            base_p = data_corrente.replace(hour=13, minute=0, second=0, microsecond=0)
            u1 = base_p + timedelta(minutes=random.randint(-5, 15))
            timbrature.append([u1.strftime("%Y-%m-%d %H:%M"), "Uscita"])
            
            # --- TURNO POMERIGGIO ---
            # Punto di riferimento: ore 14:00.
            base_pm = data_corrente.replace(hour=14, minute=0, second=0, microsecond=0)
            e2 = base_pm + timedelta(minutes=random.randint(-10, 15))
            timbrature.append([e2.strftime("%Y-%m-%d %H:%M"), "Entrata"])
            
            # Punto di riferimento: ore 18:00.
            base_ps = data_corrente.replace(hour=18, minute=0, second=0, microsecond=0)
            u2 = base_ps + timedelta(minutes=random.randint(-5, 30))
            timbrature.append([u2.strftime("%Y-%m-%d %H:%M"), "Uscita"])
            
        data_corrente += timedelta(days=1)

    # Scrittura con separatore ";" compatibile con il parsing del server 
    try:
        with open(file_path, mode='w', newline='', encoding='utf-8') as f:
            writer = csv.writer(f, delimiter=';')
            writer.writerows(timbrature)
        print(f"Completato! File '{file_path}' generato con {len(timbrature)} righe.")
    except Exception as e:
        print(f"Errore durante la scrittura del file: {e}")

# Avvio simulazione
genera_timbrature_storico("12345", anni_durata=10)