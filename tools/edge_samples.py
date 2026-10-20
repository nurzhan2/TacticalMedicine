"""Voice samples with Microsoft Edge neural TTS (free, no key): python tools/edge_samples.py <out_dir>"""
import asyncio, sys, os
import edge_tts

OUT = sys.argv[1] if len(sys.argv) > 1 else "voice_samples"
os.makedirs(OUT, exist_ok=True)
SAMPLES = [
    ("1_operator_ru_Dmitry", "ru-RU-DmitryNeural", "-5%", "-8Hz", "Принял, трёхсотый. Эвакуация выдвигается к вам. Оставайся в укрытии, будь на связи."),
    ("2_operator_kz_Daulet", "kk-KZ-DauletNeural", "-5%", "-8Hz", "Қабылдадым. Эвакуация тобы сендерге келе жатыр. Баспанада қал, байланыста бол."),
    ("3_instructor_ru_Dmitry", "ru-RU-DmitryNeural", "+0%", "+0Hz", "Под огнём только жгут. Высоко и туго, у самого паха. Затяни, крути вороток, зафиксируй."),
    ("4_instructor_ru_Svetlana", "ru-RU-SvetlanaNeural", "+0%", "+0Hz", "Под огнём только жгут. Высоко и туго, у самого паха. Затяни, крути вороток, зафиксируй."),
    ("5_instructor_kz_Aigul", "kk-KZ-AigulNeural", "+0%", "+0Hz", "Оқ астында тек жгут. Жоғары әрі қатты, шаптың дәл астына. Тарт, бұра, бекіт."),
]

async def main():
    for name, voice, rate, pitch, text in SAMPLES:
        await edge_tts.Communicate(text, voice, rate=rate, pitch=pitch).save(os.path.join(OUT, name + ".mp3"))
        print("ok", name)

asyncio.run(main())
