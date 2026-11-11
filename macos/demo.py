"""Synthetic QA data only; never import a user's calendar to generate examples."""
from core import days_for_month, now


def seed(store):
    month='2026-11'; days=days_for_month(month)
    source=dict(id='demo@example.invalid',name='예시 캘린더',color='#607eb5',enabled=True)
    store.options.update(mode='id',id_calendars=[source],setup_completed=True,auto_sync=False,
                         desktop=False,hour_reminders=False,daily_summary=False,opacity=100)
    events=[]
    examples=[('팀 회의','💬'),('디자인 작업','🎨'),('생일 파티','🎂'),('휴가','🌴'),('병원 검진','🩺'),('출장','✈️')]
    for i in range(1,31):
        if i%4==0: continue
        title,icon=examples[i%len(examples)]; day=f'2026-11-{i:02}'
        events.append(dict(id='demo-'+str(i),uid='demo-'+str(i),date=day,title=title,time='오전 10시',
                           location='',calendarId=source['id'],calendarName=source['name'],color=source['color'],
                           start=day+'T10:00:00+09:00',allDay=False,sort=600))
    store.data['months'][month]={'events':events,'syncedAt':now().isoformat()}
